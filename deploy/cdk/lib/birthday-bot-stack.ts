import {
  Stack, StackProps, CfnOutput, RemovalPolicy,
  aws_ec2 as ec2, aws_iam as iam, aws_ssm as ssm,
  aws_ecr as ecr
} from 'aws-cdk-lib';
import { Construct } from 'constructs';

type Params = {
  domainName: string;
  ecrRepo: string;
  imageTag: string;
  parameterPaths: { botToken: string; webhookSecret: string; mongoUri: string; };
};

// Constants for secrets
const SECRET_PATH_PREFIX = 'birthday-bot/';

export class BirthdayBotStack extends Stack {
  constructor(scope: Construct, id: string, props: StackProps & Params) {
    super(scope, id, props);

    const { domainName, ecrRepo, imageTag, parameterPaths } = props;

    // --- ECR Repository (import existing) ---
    // Use existing repository created by GitHub Actions
    const repository = ecr.Repository.fromRepositoryName(
      this,
      'BirthdayBotRepo',
      ecrRepo
    );

    // --- Security Groups ---
    const vpc = ec2.Vpc.fromLookup(this, 'DefaultVpc', { isDefault: true });
    
    // Bot Security Group
    const botSg = new ec2.SecurityGroup(this, 'BotSg', {
      vpc,
      description: 'Allow HTTP/HTTPS for Caddy',
      allowAllOutbound: true
    });
    // HTTP for ACME Let's Encrypt challenge
    botSg.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(80), 'HTTP for ACME');
    botSg.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(443), 'HTTPS for webhook');
    
    // IPv6 support (optional)
    botSg.addIngressRule(ec2.Peer.anyIpv6(), ec2.Port.tcp(80), 'HTTP IPv6 for ACME');
    botSg.addIngressRule(ec2.Peer.anyIpv6(), ec2.Port.tcp(443), 'HTTPS IPv6 for webhook');

    // --- IAM Role for EC2 ---
    const role = new iam.Role(this, 'Ec2Role', {
      assumedBy: new iam.ServicePrincipal('ec2.amazonaws.com')
    });

    role.addManagedPolicy(
      iam.ManagedPolicy.fromAwsManagedPolicyName('AmazonSSMManagedInstanceCore')
    );
    role.addManagedPolicy(
      iam.ManagedPolicy.fromAwsManagedPolicyName('AmazonEC2ContainerRegistryReadOnly')
    );
    role.addManagedPolicy(
      iam.ManagedPolicy.fromAwsManagedPolicyName('CloudWatchAgentServerPolicy')
    );

    // Permissions to read SSM parameters (SecureString without custom KMS is OK)
    role.addToPolicy(new iam.PolicyStatement({
      actions: ['ssm:GetParameter', 'ssm:GetParameters', 'ssm:GetParametersByPath'],
      resources: [
        `arn:aws:ssm:${this.region}:${this.account}:parameter${parameterPaths.botToken}`,
        `arn:aws:ssm:${this.region}:${this.account}:parameter${parameterPaths.webhookSecret}`,
        `arn:aws:ssm:${this.region}:${this.account}:parameter${parameterPaths.mongoUri}`
      ]
    }));

    // Permissions for ECR image pull
    repository.grantPull(role);

    // Permissions to read secrets from Secrets Manager
    // Note: AWS Secrets Manager ARN format: arn:aws:secretsmanager:region:account:secret:name-6RandomChars
    // Wildcard covers all secrets starting with 'birthday-bot/' (including birthday-bot/all-config-XXXXXX)
    role.addToPolicy(new iam.PolicyStatement({
      sid: 'ReadSecretsForBirthdayBot',
      actions: ['secretsmanager:GetSecretValue'],
      resources: [
        `arn:aws:secretsmanager:${this.region}:${this.account}:secret:${SECRET_PATH_PREFIX}*`
      ]
    }));

    const profile = new iam.CfnInstanceProfile(this, 'InstanceProfile', {
      roles: [role.roleName]
    });

    // --- AMI: Amazon Linux 2023 Arm64 ---
    const amzn2023Arm = ec2.MachineImage.latestAmazonLinux2023({
      cachedInContext: true,
      cpuType: ec2.AmazonLinuxCpuType.ARM_64
    });

    // --- UserData (Docker + установка скриптов + запуск сервисов) ---
    const repoUri = `${this.account}.dkr.ecr.${this.region}.amazonaws.com/${ecrRepo}`;
    
    const userData = ec2.UserData.forLinux();
    userData.addCommands(
      'set -euxo pipefail',
      'dnf update -y',
      'dnf install -y docker jq curl unzip',
      'systemctl enable --now docker',
      'usermod -aG docker ec2-user || true',

      // AWS CLI v2 (если не установлен)
      'if ! command -v aws >/dev/null 2>&1; then',
      '  curl "https://awscli.amazonaws.com/awscli-exe-linux-aarch64.zip" -o "/tmp/awscliv2.zip"',
      '  unzip -q /tmp/awscliv2.zip -d /tmp && sudo /tmp/aws/install',
      'fi',

      // Login to ECR
      `aws ecr get-login-password --region ${this.region} | docker login --username AWS --password-stdin ${this.account}.dkr.ecr.${this.region}.amazonaws.com`,

      // Install Docker Compose
      'curl -L "https://github.com/docker/compose/releases/download/v2.29.2/docker-compose-linux-aarch64" -o /usr/local/bin/docker-compose',
      'chmod +x /usr/local/bin/docker-compose',

      // Install Git
      'dnf install -y git',

      // Clone repository
      'mkdir -p /opt',
      'cd /opt && git clone https://github.com/HromykoIvan/BirthdayHelper.git birthday || true',
      'cd /opt/birthday && git checkout master && git pull --rebase || true',
      'chown -R ec2-user:ec2-user /opt/birthday',

      // Set environment variables
      `echo "REGION=${this.region}" | sudo tee -a /etc/environment`,
      `echo "DOMAIN=${domainName}" | sudo tee -a /etc/environment`,
      `echo "ECR_REPO=${ecrRepo}" | sudo tee -a /etc/environment`,
      'source /etc/environment',

      // Install systemd unit
      'cp /opt/birthday/ops/birthday.service /etc/systemd/system/birthday.service',
      'chmod +x /opt/birthday/ops/env-from-secrets.sh /opt/birthday/ops/deploy.sh',
      'systemctl daemon-reload',
      'systemctl enable birthday',
      'systemctl start birthday'
    );

    // --- EC2 Instance for Bot ---
    const instance = new ec2.Instance(this, 'BotInstance', {
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      securityGroup: botSg,
      role,
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T4G, ec2.InstanceSize.MICRO),
      machineImage: amzn2023Arm,
      userData,
      ssmSessionPermissions: true
    });
    (instance.node.defaultChild as ec2.CfnInstance).iamInstanceProfile = profile.ref;

    // --- SSM Parameter for GitHub Actions ---
    const botInstanceParam = new ssm.StringParameter(this, 'BotInstanceIdParam', {
      parameterName: '/birthday-bot/bot-instance-id',
      stringValue: instance.instanceId,
      description: 'Bot EC2 Instance ID for GitHub Actions deployment',
    });
    // Keep parameter across stack replacement/removal.
    (botInstanceParam.node.defaultChild as ssm.CfnParameter).applyRemovalPolicy(RemovalPolicy.RETAIN);

    // --- Outputs ---
    new CfnOutput(this, 'PublicIp', { value: instance.instancePublicIp });
    new CfnOutput(this, 'InstanceId', { 
      value: instance.instanceId,
      exportName: 'BirthdayBot-InstanceId',
      description: 'Bot EC2 Instance ID'
    });
    new CfnOutput(this, 'EcrRepoUri', { value: repository.repositoryUri });
  }
}
