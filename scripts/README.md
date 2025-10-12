# DevOps Scripts

Collection of scripts for secure access and deployment via AWS SSM Session Manager.

## Scripts Overview

### MongoDB Access (Windows - PowerShell)
- `mongo-tunnel-ssm.ps1` - Open SSM port forwarding to MongoDB
- `mongo-uri.ps1` - Get MongoDB connection string
- `mongo-compass.ps1` - Auto-open MongoDB Compass

### Deployment (Linux/macOS - Bash)
- `resolve-instance.sh` - Resolve Bot EC2 Instance ID
- `rollout.sh` - Deploy Docker image to EC2 via SSM

---

# MongoDB SSM Tunnel (Windows)

PowerShell scripts for secure MongoDB access via AWS SSM Session Manager port forwarding.

## Prerequisites

### 1. Install Required Tools

**AWS CLI v2**
```powershell
# Check version
aws --version
```
Download from: https://aws.amazon.com/cli/

**Session Manager Plugin**
```powershell
# Check if installed
session-manager-plugin --version
```
Download from: https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html

**PowerShell 7** (recommended)
```powershell
# Check version
pwsh --version
```
Download from: https://github.com/PowerShell/PowerShell/releases

> **Note**: If using Windows PowerShell 5, change `pwsh` to `powershell` in `package.json` scripts.

### 2. AWS IAM Permissions

Your local AWS user/role needs these permissions:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "ssm:StartSession",
        "ssm:TerminateSession",
        "ssm:DescribeSessions",
        "ssm:DescribeInstanceInformation"
      ],
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": "ec2:DescribeInstances",
      "Resource": "*"
    },
    {
      "Effect": "Allow",
      "Action": "secretsmanager:GetSecretValue",
      "Resource": "*"
    }
  ]
}
```

### 3. Configure AWS Profile

```powershell
# Configure default profile
aws configure

# Or use SSO
aws configure sso
```

## Usage

### Open MongoDB Tunnel

In PowerShell (from project root):

```powershell
npm run mongo:tunnel
```

Keep this window **open**. Press `Ctrl+C` to stop.

### Get MongoDB URI

In a **new** PowerShell window:

```powershell
npm run mongo:uri
```

Output example:
```
mongodb://localhost:27017/birthdaybot?directConnection=true
```

### Open MongoDB Compass

```powershell
npm run mongo:compass
```

This will:
1. Retrieve the connection string from AWS Secrets Manager
2. Convert it to local tunnel URI
3. Attempt to open MongoDB Compass automatically

If Compass doesn't open, copy the URI manually from `npm run mongo:uri`.

## Configuration

### Change EC2 Tag

If your MongoDB EC2 instance has a different Name tag:

**Option 1**: Edit `scripts/mongo-tunnel-ssm.ps1`:
```powershell
[string]$Ec2NameTag = "YourCustomTag"
```

**Option 2**: Pass as environment variable:
```powershell
$env:STACK_TAG="YourCustomTag"
npm run mongo:tunnel
```

### Change AWS Region

```powershell
$env:AWS_REGION="us-east-1"
npm run mongo:tunnel
```

### Change Local Port

Edit `scripts/mongo-tunnel-ssm.ps1`:
```powershell
[int]$LocalPort = 27018
```

## Troubleshooting

### "Instance not found"
- Check that MongoDB EC2 is running
- Verify the Name tag matches: `BirthdayBotStack/MongoInstance`
- Confirm AWS region is correct

### "session-manager-plugin not found"
Install the Session Manager Plugin (see Prerequisites)

### "Failed to read secret"
- Check AWS credentials: `aws sts get-caller-identity`
- Verify IAM permissions for `secretsmanager:GetSecretValue`
- Confirm secret name: `birthday-bot/mongo-url`

### Compass TLS/SSL errors
We're connecting via `localhost:27017` (no TLS). Disable TLS/SSL in Compass connection settings.

### Port already in use
```powershell
# Find process using port 27017
netstat -ano | findstr :27017

# Kill the process (replace PID)
taskkill /PID <PID> /F
```

## Security Notes

✅ **Secure**: Connection goes through AWS SSM, no need to open Security Groups
✅ **No credentials in code**: URI retrieved from AWS Secrets Manager
✅ **No SSH keys**: Uses IAM authentication via AWS CLI
✅ **Encrypted**: SSM session traffic is encrypted by AWS

❌ **Don't**: Commit AWS credentials to git
❌ **Don't**: Open MongoDB port (27017) in Security Groups
❌ **Don't**: Share connection strings in chat/email

---

# Deployment Scripts (Linux/macOS)

Bash scripts for deploying Docker images to EC2 via SSM.

## Prerequisites

- AWS CLI v2
- Session Manager Plugin
- Bash shell (Linux/macOS/WSL)
- AWS credentials configured

## Usage

### Resolve Instance ID

```bash
./scripts/resolve-instance.sh
```

Output: `i-0abc123def456789`

This script:
1. First tries SSM parameter: `/birthday-bot/bot-instance-id`
2. Falls back to EC2 tag: `Name=BirthdayBotStack/BotInstance`

### Deploy Image

```bash
# Deploy latest tag
./scripts/rollout.sh

# Deploy specific tag
./scripts/rollout.sh v1.2.3

# Deploy with full URI
./scripts/rollout.sh 123456789012.dkr.ecr.eu-central-1.amazonaws.com/birthday-helper:sha-abc123
```

The script will:
1. Resolve the EC2 instance ID
2. Login to ECR
3. Pull the specified image
4. Restart the container
5. Clean up old images

### Environment Variables

```bash
# Change AWS region
export AWS_REGION=us-east-1

# Change ECR repository name
export ECR_REPO=my-custom-repo

# Run deployment
./scripts/rollout.sh
```

## Make Scripts Executable

```bash
chmod +x scripts/resolve-instance.sh
chmod +x scripts/rollout.sh
```

## Integration with CI/CD

These scripts are used by GitHub Actions workflows:
- `.github/workflows/build-and-push.yml` - Uses same logic for rollout job
- Ensures consistent deployment behavior between local and CI/CD

## Troubleshooting

### "Instance not found"
Check that:
- EC2 instance is running
- SSM parameter `/birthday-bot/bot-instance-id` exists OR
- EC2 tag `Name=BirthdayBotStack/BotInstance` is set

### "Command execution failed"
View command output:
```bash
# Get command ID from script output
aws ssm get-command-invocation \
  --command-id <COMMAND_ID> \
  --instance-id <INSTANCE_ID> \
  --region eu-central-1
```

### "Permission denied"
Ensure your IAM user/role has:
- `ssm:SendCommand`
- `ssm:GetCommandInvocation`
- `ec2:DescribeInstances`
- `ssm:GetParameter`

