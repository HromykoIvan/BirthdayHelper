.PHONY: build test run docker-build docker-push cdk-help cdk-install cdk-deploy cdk-destroy

build:
	dotnet build BirthdayBot.sln

test:
	dotnet test BirthdayBot.sln

run:
	dotnet run --project backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj

docker-build:
	docker build -t birthday-bot:local .

docker-push:
	@echo "Use CI to push to ECR"

# CDK commands
cdk-help:
	@echo "CDK Commands:"
	@echo "  cdk-install  - Install CDK dependencies"
	@echo "  cdk-deploy   - Deploy AWS infrastructure"
	@echo "  cdk-destroy  - Destroy AWS infrastructure"
	@echo ""
	@echo "Required environment variables:"
	@echo "  DOMAIN_NAME          - Your domain name"
	@echo "  CDK_DEFAULT_ACCOUNT  - AWS Account ID"
	@echo "  CDK_DEFAULT_REGION   - AWS Region"
	@echo "  ECR_REPO            - ECR repository name (optional, default: birthday-helper)"
	@echo "  IMAGE_TAG           - Docker image tag (optional, default: latest)"

cdk-install:
	cd deploy/cdk && npm install

cdk-deploy:
	cd deploy/cdk && $(MAKE) deploy-full

cdk-destroy:
	cd deploy/cdk && $(MAKE) destroy