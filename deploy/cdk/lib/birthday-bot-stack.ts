import {
  Stack, StackProps, CfnOutput, Duration, aws_ec2 as ec2,
  aws_iam as iam, aws_ssm as ssm
} from 'aws-cdk-lib';
import { Construct } from 'constructs';

type Params = {
  domainName: string;
  ecrRepo: string;
  imageTag: string;
  parameterPaths: { botToken: string; webhookSecret: string; mongoUri: string; };
};

export class BirthdayBotStack extends Stack {
  constructor(scope: Construct, id: string, props: StackProps & Params) {
    super(scope, id, props);

    const { domainName, ecrRepo, imageTag, parameterPaths } = props;

    // --- Security Group ---
    const vpc = ec2.Vpc.fromLookup(this, 'DefaultVpc', { isDefault: true });
    const sg = new ec2.SecurityGroup(this, 'BotSg', {
      vpc,
      description: 'Allow HTTP/HTTPS for Caddy',
      allowAllOutbound: true
    });
    // HTTP для ACME проверки Let's Encrypt
    sg.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(80), 'HTTP for ACME');
    sg.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(443), 'HTTPS for webhook');
    
    // Поддержка IPv6 (опционально)
    sg.addIngressRule(ec2.Peer.anyIpv6(), ec2.Port.tcp(80), 'HTTP IPv6 for ACME');
    sg.addIngressRule(ec2.Peer.anyIpv6(), ec2.Port.tcp(443), 'HTTPS IPv6 for webhook');

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

    // Права читать SSM параметры (SecureString без кастомного KMS — ок)
    role.addToPolicy(new iam.PolicyStatement({
      actions: ['ssm:GetParameter', 'ssm:GetParameters', 'ssm:GetParametersByPath'],
      resources: [
        `arn:aws:ssm:${this.region}:${this.account}:parameter${parameterPaths.botToken}`,
        `arn:aws:ssm:${this.region}:${this.account}:parameter${parameterPaths.webhookSecret}`,
        `arn:aws:ssm:${this.region}:${this.account}:parameter${parameterPaths.mongoUri}`
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

    // --- UserData (Docker + Caddy + запуск контейнера) ---
    const userData = ec2.UserData.forLinux();
    userData.addCommands(
      'set -euxo pipefail',
      'dnf update -y',
      'dnf install -y docker awscli jq',
      "dnf install -y 'dnf-command(copr)'",
      'dnf copr enable -y @caddy/caddy',
      'dnf install -y caddy',
      'systemctl enable --now docker',
      'usermod -aG docker ec2-user || true',

      // логин в ECR
      `aws ecr get-login-password --region ${this.region} | docker login --username AWS --password-stdin ${this.account}.dkr.ecr.${this.region}.amazonaws.com`,

      // достаём секреты из SSM
      `BOT_TOKEN=$(aws ssm get-parameter --name "${parameterPaths.botToken}" --with-decryption --query Parameter.Value --output text --region ${this.region})`,
      `WEBHOOK_SECRET=$(aws ssm get-parameter --name "${parameterPaths.webhookSecret}" --with-decryption --query Parameter.Value --output text --region ${this.region} || echo "")`,
      `MONGODB_URI=$(aws ssm get-parameter --name "${parameterPaths.mongoUri}" --with-decryption --query Parameter.Value --output text --region ${this.region})`,

      'cat >/etc/birthday-bot.env <<EOF',
      'TELEGRAM_BOT_TOKEN=$BOT_TOKEN',
      'TELEGRAM_WEBHOOK_SECRET=$WEBHOOK_SECRET',
      'MONGODB_URI=$MONGODB_URI',
      'ASPNETCORE_ENVIRONMENT=Production',
      'EOF',
      'chmod 600 /etc/birthday-bot.env',

      // systemd unit
      'cat >/etc/systemd/system/birthday-bot.service <<EOF',
      '[Unit]',
      'Description=BirthdayBot container',
      'After=docker.service',
      'Requires=docker.service',
      '',
      '[Service]',
      'EnvironmentFile=/etc/birthday-bot.env',
      'Restart=always',
      `ExecStartPre=/usr/bin/docker pull ${this.account}.dkr.ecr.${this.region}.amazonaws.com/${ecrRepo}:${imageTag}`,
      `ExecStart=/usr/bin/docker run --rm --name birthday-bot -p 8080:8080 --env-file /etc/birthday-bot.env ${this.account}.dkr.ecr.${this.region}.amazonaws.com/${ecrRepo}:${imageTag}`,
      'ExecStop=/usr/bin/docker stop birthday-bot',
      '',
      '[Install]',
      'WantedBy=multi-user.target',
      'EOF',

      // Caddyfile
      'mkdir -p /etc/caddy',
      `cat >/etc/caddy/Caddyfile <<EOF
${domainName} {
  encode gzip
  reverse_proxy 127.0.0.1:8080
}
EOF`,

      'systemctl daemon-reload',
      'systemctl enable --now birthday-bot',
      'systemctl enable --now caddy'
    );

    // --- EC2 Instance ---
    const instance = new ec2.Instance(this, 'BotInstance', {
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      securityGroup: sg,
      role,
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T4G, ec2.InstanceSize.MICRO),
      machineImage: amzn2023Arm,
      userData,
      ssmSessionPermissions: true
    });
    (instance.node.defaultChild as ec2.CfnInstance).iamInstanceProfile = profile.ref;

    new CfnOutput(this, 'PublicIp', { value: instance.instancePublicIp });
    new CfnOutput(this, 'InstanceId', { value: instance.instanceId });
  }
}
