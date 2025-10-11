# Birthday Bot CDK Makefile

.PHONY: help install bootstrap synth deploy destroy clean lint

# Default target
help:
	@echo "Birthday Bot CDK Commands:"
	@echo "  install     - Install npm dependencies"
	@echo "  bootstrap   - Bootstrap CDK environment"
	@echo "  synth       - Synthesize CloudFormation template"
	@echo "  deploy      - Deploy the stack"
	@echo "  destroy     - Destroy the stack"
	@echo "  clean       - Clean build artifacts"
	@echo "  lint        - Run TypeScript linter"
	@echo "  diff        - Show differences between deployed stack and current state"
	@echo ""
	@echo "Environment variables:"
	@echo "  DOMAIN_NAME          - Your domain name (required)"
	@echo "  ECR_REPO            - ECR repository name (default: birthday-helper)"
	@echo "  IMAGE_TAG           - Docker image tag (default: latest)"
	@echo "  CDK_DEFAULT_ACCOUNT - AWS Account ID (required)"
	@echo "  CDK_DEFAULT_REGION  - AWS Region (required)"

install:
	@echo "📦 Installing dependencies..."
	npm install

bootstrap:
	@echo "🔧 Bootstrapping CDK..."
	cdk bootstrap --require-approval never

synth:
	@echo "🔍 Synthesizing stack..."
	npm run synth

deploy:
	@echo "🚀 Deploying stack..."
	npm run deploy

destroy:
	@echo "💥 Destroying stack..."
	cdk destroy

clean:
	@echo "🧹 Cleaning build artifacts..."
	rm -rf dist/
	rm -rf cdk.out/
	rm -rf node_modules/

lint:
	@echo "🔍 Running TypeScript linter..."
	npx tsc --noEmit

diff:
	@echo "📊 Showing stack differences..."
	cdk diff

# Validate environment variables
validate-env:
	@if [ -z "$(DOMAIN_NAME)" ]; then \
		echo "❌ DOMAIN_NAME environment variable is required"; \
		exit 1; \
	fi
	@if [ -z "$(CDK_DEFAULT_ACCOUNT)" ]; then \
		echo "❌ CDK_DEFAULT_ACCOUNT environment variable is required"; \
		exit 1; \
	fi
	@if [ -z "$(CDK_DEFAULT_REGION)" ]; then \
		echo "❌ CDK_DEFAULT_REGION environment variable is required"; \
		exit 1; \
	fi

# Full deployment pipeline
deploy-full: validate-env install bootstrap synth deploy
	@echo "✅ Full deployment completed!"

# Quick deployment (assumes CDK is already bootstrapped)
deploy-quick: validate-env install synth deploy
	@echo "✅ Quick deployment completed!"
