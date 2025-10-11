#!/usr/bin/env node
import 'source-map-support/register';
import { App } from 'aws-cdk-lib';
import { BirthdayBotStack } from '../lib/birthday-bot-stack';

const app = new App();

// ВАЖНО: задай значения через контекст (CDK context) или GitHub Actions env/vars
const stack = new BirthdayBotStack(app, 'BirthdayBotStack', {
  env: {
    account: process.env.CDK_DEFAULT_ACCOUNT,
    region: process.env.CDK_DEFAULT_REGION
  },

  domainName: process.env.DOMAIN_NAME ?? 'bot.example.com',    // твой домен
  ecrRepo: process.env.ECR_REPO ?? 'birthday-helper',           // имя репо
  imageTag: process.env.IMAGE_TAG ?? 'latest',                  // тег
  parameterPaths: {                                             // пути в SSM
    botToken: '/birthday-bot/BOT_TOKEN',
    webhookSecret: '/birthday-bot/WEBHOOK_SECRET',
    mongoUri: '/birthday-bot/MONGODB_URI'
  }
});
