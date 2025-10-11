.PHONY: build test run docker-build docker-push helm-install helm-upgrade helm-uninstall kube-helpers cdk-help cdk-install cdk-deploy cdk-destroy

build:
\tdotnet build BirthdayBot.sln

test:
\tdotnet test BirthdayBot.sln

run:
\tdotnet run --project backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj

docker-build:
\tdocker build -t birthday-bot:local .

docker-push:
\t@echo "Use CI to push to ECR"

helm-install:
\thelm upgrade --install birthday-bot deploy/helm/birthday-bot --namespace birthday-bot --create-namespace

helm-upgrade:
\thelm upgrade birthday-bot deploy/helm/birthday-bot --namespace birthday-bot

helm-uninstall:
\thelm uninstall birthday-bot --namespace birthday-bot

kube-helpers:
\tkubectl -n birthday-bot get pods,svc,ingress