#!/usr/bin/env bash
set -euo pipefail

PROJECT_ID="birthdayhelper-511100"
REGION="europe-central2"
GITHUB_REPO="HromykoIvan/BirthdayHelper"

ARTIFACT_REPOSITORY="birthday-helper"
DEPLOY_SERVICE_ACCOUNT="github-deployer"
RUNTIME_SERVICE_ACCOUNT="birthday-helper-runtime"
WIF_POOL="github"
WIF_PROVIDER="birthdayhelper"

SECRET_TELEGRAM_TOKEN="birthday-helper-telegram-token"
SECRET_TELEGRAM_WEBHOOK="birthday-helper-telegram-webhook-secret"
SECRET_MONGODB_URI="birthday-helper-mongodb-uri"
SECRET_SCHEDULER="birthday-helper-scheduler-secret"

DEPLOY_SA_EMAIL="${DEPLOY_SERVICE_ACCOUNT}@${PROJECT_ID}.iam.gserviceaccount.com"
RUNTIME_SA_EMAIL="${RUNTIME_SERVICE_ACCOUNT}@${PROJECT_ID}.iam.gserviceaccount.com"

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required command not found: $1" >&2
    exit 1
  }
}

ensure_service_account() {
  local name="$1"
  local display_name="$2"
  local email="${name}@${PROJECT_ID}.iam.gserviceaccount.com"

  if ! gcloud iam service-accounts describe "$email" --project="$PROJECT_ID" >/dev/null 2>&1; then
    gcloud iam service-accounts create "$name"       --project="$PROJECT_ID"       --display-name="$display_name"
  fi
}

ensure_secret() {
  local name="$1"

  if ! gcloud secrets describe "$name" --project="$PROJECT_ID" >/dev/null 2>&1; then
    gcloud secrets create "$name"       --project="$PROJECT_ID"       --replication-policy="automatic"
  fi
}

secret_has_enabled_version() {
  local name="$1"
  [[ -n "$(gcloud secrets versions list "$name"     --project="$PROJECT_ID"     --filter='state=ENABLED'     --format='value(name)'     --limit=1)" ]]
}

add_secret_value() {
  local name="$1"
  local value="$2"
  printf '%s' "$value" | gcloud secrets versions add "$name"     --project="$PROJECT_ID"     --data-file=-
}

require_command gcloud
require_command openssl

gcloud config set project "$PROJECT_ID" >/dev/null

echo "Enabling required Google Cloud APIs..."
gcloud services enable   run.googleapis.com   artifactregistry.googleapis.com   secretmanager.googleapis.com   cloudscheduler.googleapis.com   iam.googleapis.com   iamcredentials.googleapis.com   sts.googleapis.com   --project="$PROJECT_ID"

PROJECT_NUMBER="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')"

echo "Creating service accounts..."
ensure_service_account "$DEPLOY_SERVICE_ACCOUNT" "BirthdayHelper GitHub deployer"
ensure_service_account "$RUNTIME_SERVICE_ACCOUNT" "BirthdayHelper Cloud Run runtime"

echo "Creating Artifact Registry repository..."
if ! gcloud artifacts repositories describe "$ARTIFACT_REPOSITORY"   --project="$PROJECT_ID"   --location="$REGION" >/dev/null 2>&1; then
  gcloud artifacts repositories create "$ARTIFACT_REPOSITORY"     --project="$PROJECT_ID"     --location="$REGION"     --repository-format="docker"     --description="BirthdayHelper container images"
fi

echo "Creating Secret Manager secrets..."
for secret in   "$SECRET_TELEGRAM_TOKEN"   "$SECRET_TELEGRAM_WEBHOOK"   "$SECRET_MONGODB_URI"   "$SECRET_SCHEDULER"; do
  ensure_secret "$secret"
done

if ! secret_has_enabled_version "$SECRET_TELEGRAM_TOKEN"; then
  echo
  read -r -s -p "Paste the NEW Telegram bot token: " TELEGRAM_TOKEN
  echo
  if [[ -z "$TELEGRAM_TOKEN" ]]; then
    echo "Telegram token cannot be empty." >&2
    exit 1
  fi
  add_secret_value "$SECRET_TELEGRAM_TOKEN" "$TELEGRAM_TOKEN"
  unset TELEGRAM_TOKEN
fi

if ! secret_has_enabled_version "$SECRET_MONGODB_URI"; then
  echo
  read -r -s -p "Paste the MongoDB Atlas URI for birthday-helper-app: " MONGODB_URI
  echo
  if [[ -z "$MONGODB_URI" ]]; then
    echo "MongoDB URI cannot be empty." >&2
    exit 1
  fi
  add_secret_value "$SECRET_MONGODB_URI" "$MONGODB_URI"
  unset MONGODB_URI
fi

if ! secret_has_enabled_version "$SECRET_TELEGRAM_WEBHOOK"; then
  add_secret_value "$SECRET_TELEGRAM_WEBHOOK" "$(openssl rand -hex 32)"
fi

if ! secret_has_enabled_version "$SECRET_SCHEDULER"; then
  add_secret_value "$SECRET_SCHEDULER" "$(openssl rand -hex 32)"
fi

echo "Granting runtime access to secrets..."
for secret in   "$SECRET_TELEGRAM_TOKEN"   "$SECRET_TELEGRAM_WEBHOOK"   "$SECRET_MONGODB_URI"   "$SECRET_SCHEDULER"; do
  gcloud secrets add-iam-policy-binding "$secret"     --project="$PROJECT_ID"     --member="serviceAccount:${RUNTIME_SA_EMAIL}"     --role="roles/secretmanager.secretAccessor" >/dev/null

  gcloud secrets add-iam-policy-binding "$secret"     --project="$PROJECT_ID"     --member="serviceAccount:${DEPLOY_SA_EMAIL}"     --role="roles/secretmanager.secretAccessor" >/dev/null
done

echo "Granting GitHub deployer permissions..."
for role in   roles/run.admin   roles/cloudscheduler.admin   roles/serviceusage.serviceUsageConsumer   roles/secretmanager.viewer; do
  gcloud projects add-iam-policy-binding "$PROJECT_ID"     --member="serviceAccount:${DEPLOY_SA_EMAIL}"     --role="$role"     --quiet >/dev/null
done

gcloud artifacts repositories add-iam-policy-binding "$ARTIFACT_REPOSITORY"   --project="$PROJECT_ID"   --location="$REGION"   --member="serviceAccount:${DEPLOY_SA_EMAIL}"   --role="roles/artifactregistry.writer" >/dev/null

gcloud iam service-accounts add-iam-policy-binding "$RUNTIME_SA_EMAIL"   --project="$PROJECT_ID"   --member="serviceAccount:${DEPLOY_SA_EMAIL}"   --role="roles/iam.serviceAccountUser" >/dev/null

echo "Creating GitHub Workload Identity Federation..."
if ! gcloud iam workload-identity-pools describe "$WIF_POOL"   --project="$PROJECT_ID"   --location="global" >/dev/null 2>&1; then
  gcloud iam workload-identity-pools create "$WIF_POOL"     --project="$PROJECT_ID"     --location="global"     --display-name="GitHub Actions"
fi

if ! gcloud iam workload-identity-pools providers describe "$WIF_PROVIDER"   --project="$PROJECT_ID"   --location="global"   --workload-identity-pool="$WIF_POOL" >/dev/null 2>&1; then
  gcloud iam workload-identity-pools providers create-oidc "$WIF_PROVIDER"     --project="$PROJECT_ID"     --location="global"     --workload-identity-pool="$WIF_POOL"     --display-name="BirthdayHelper GitHub"     --issuer-uri="https://token.actions.githubusercontent.com"     --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref"     --attribute-condition="assertion.repository=='${GITHUB_REPO}'"
fi

POOL_NAME="$(gcloud iam workload-identity-pools describe "$WIF_POOL"   --project="$PROJECT_ID"   --location="global"   --format='value(name)')"

PROVIDER_NAME=""
for attempt in 1 2 3 4 5 6; do
  PROVIDER_NAME="$(gcloud iam workload-identity-pools providers describe "$WIF_PROVIDER" --project="$PROJECT_ID" --location="global" --workload-identity-pool="$WIF_POOL" --format='value(name)' 2>/dev/null || true)"

  if [[ -n "$PROVIDER_NAME" ]]; then
    break
  fi

  echo "Waiting for Workload Identity provider to become available... attempt ${attempt}/6"
  sleep 10
done

if [[ -z "$PROVIDER_NAME" ]]; then
  echo "Workload Identity provider was created but is not readable yet." >&2
  echo "Re-run this bootstrap script in a minute; it is safe and idempotent." >&2
  exit 1
fi

gcloud iam service-accounts add-iam-policy-binding "$DEPLOY_SA_EMAIL"   --project="$PROJECT_ID"   --role="roles/iam.workloadIdentityUser"   --member="principalSet://iam.googleapis.com/${POOL_NAME}/attribute.repository/${GITHUB_REPO}" >/dev/null

echo
echo "Bootstrap complete."
echo
echo "GitHub repository variable to create:"
echo "GCP_WORKLOAD_IDENTITY_PROVIDER=${PROVIDER_NAME}"
echo
echo "Fixed deployment settings:"
echo "GCP project: ${PROJECT_ID}"
echo "Region: ${REGION}"
echo "Deploy service account: ${DEPLOY_SA_EMAIL}"
echo "Runtime service account: ${RUNTIME_SA_EMAIL}"
echo
echo "No Google Cloud service-account key was created."
