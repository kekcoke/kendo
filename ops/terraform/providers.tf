# Kendo — Hetzner Cloud Terraform Configuration
# Phase 8: Production Deployment
# Provider: hetznercloud/hcloud
# Target: Single VM with Docker Compose (scalable to multi-node)

terraform {
  required_version = ">= 1.6"
  required_providers {
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.47"
    }
  }
}

provider "hcloud" {
  # HCLOUD_TOKEN environment variable must be set
  # export HCLOUD_TOKEN="your-api-token"
}
