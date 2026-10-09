# BirthdayHelper on Google Cloud

Production target:

- Google Cloud project: `birthdayhelper-511100`
- Region: `europe-central2` (Warsaw)
- Artifact Registry repository: `birthday-helper`
- Cloud Run service: `birthday-helper`
- MongoDB: MongoDB Atlas database `birthdaybot`
- Reminders: Cloud Scheduler -> `POST /internal/reminders/run`
- Telegram: Cloud Run public webhook `POST /telegram/webhook`

## One-time bootstrap

Run the bootstrap script once from Google Cloud Shell while authenticated as the project owner:

```bash
curl -fsSL https://raw.githubusercontent.com/HromykoIvan/BirthdayHelper/gcp-migration/deploy/gcp/bootstrap.sh -o /tmp/birthdayhelper-gcp-bootstrap.sh
bash /tmp/birthdayhelper-gcp-bootstrap.sh
```

The script:

- enables the required Google Cloud APIs;
- creates the Artifact Registry Docker repository;
- creates the Cloud Run runtime service account;
- creates the GitHub deployment service account;
- creates the Secret Manager secrets;
- prompts securely for the Telegram bot token and MongoDB Atlas URI;
- generates the Telegram webhook secret and scheduler secret;
- creates GitHub Workload Identity Federation without a service-account key;
- prints the exact `GCP_WORKLOAD_IDENTITY_PROVIDER` repository variable value.

The script is idempotent and can be run again. Existing secret values are not overwritten.

## GitHub repository variable

After bootstrap, create one GitHub Actions repository variable:

```text
GCP_WORKLOAD_IDENTITY_PROVIDER=<the exact provider name printed by bootstrap.sh>
```

No Google Cloud JSON key is required.

## Automatic deployment

After the migration PR is merged, every relevant push to `master` runs:

1. GitHub OIDC authentication to Google Cloud.
2. Docker build for `linux/amd64`.
3. Push to Artifact Registry.
4. Deploy a new Cloud Run revision.
5. Health check.
6. Create/update the one-minute Cloud Scheduler reminder job.
7. Update the Telegram webhook to the current Cloud Run URL.

Cloud Run is configured with `min-instances=0` and `max-instances=1`.

## Secrets

The following Secret Manager secrets are created by bootstrap:

- `birthday-helper-telegram-token`
- `birthday-helper-telegram-webhook-secret`
- `birthday-helper-mongodb-uri`
- `birthday-helper-scheduler-secret`

Sensitive values are not stored in GitHub or in this repository.
