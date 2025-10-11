#!/bin/bash

# Birthday Bot CDK Deploy Script
set -e

echo "🚀 Starting Birthday Bot CDK deployment..."

# Check if required environment variables are set
if [ -z "$DOMAIN_NAME" ]; then
    echo "❌ DOMAIN_NAME environment variable is required"
    exit 1
fi

if [ -z "$CDK_DEFAULT_ACCOUNT" ]; then
    echo "❌ CDK_DEFAULT_ACCOUNT environment variable is required"
    exit 1
fi

if [ -z "$CDK_DEFAULT_REGION" ]; then
    echo "❌ CDK_DEFAULT_REGION environment variable is required"
    exit 1
fi

# Set defaults if not provided
export ECR_REPO=${ECR_REPO:-"birthday-helper"}
export IMAGE_TAG=${IMAGE_TAG:-"latest"}

echo "📋 Deployment configuration:"
echo "  Domain: $DOMAIN_NAME"
echo "  ECR Repo: $ECR_REPO"
echo "  Image Tag: $IMAGE_TAG"
echo "  AWS Account: $CDK_DEFAULT_ACCOUNT"
echo "  AWS Region: $CDK_DEFAULT_REGION"
echo ""

# Install dependencies
echo "📦 Installing dependencies..."
npm install

# Bootstrap CDK if needed
echo "🔧 Checking CDK bootstrap..."
cdk bootstrap --require-approval never || echo "CDK already bootstrapped"

# Synthesize the stack
echo "🔍 Synthesizing stack..."
npm run synth

# Deploy the stack
echo "🚀 Deploying stack..."
npm run deploy

echo "✅ Deployment completed successfully!"
echo ""
echo "📊 Stack outputs:"
cdk list

echo ""
echo "🔗 Next steps:"
echo "1. Update your domain's DNS to point to the PublicIp output"
echo "2. Check the application logs: sudo journalctl -u birthday-bot -f"
echo "3. Verify HTTPS is working: https://$DOMAIN_NAME"
