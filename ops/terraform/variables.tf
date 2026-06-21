variable "server_name" {
  description = "Name of the Hetzner server"
  type        = string
  default     = "kendo-prod"
}

variable "server_type" {
  description = "Hetzner server type (CX22 = 2 vCPU, 4GB RAM; CX32 = 4 vCPU, 8GB RAM)"
  type        = string
  default     = "cx22"
}

variable "location" {
  description = "Hetzner datacenter location"
  type        = string
  default     = "fsn1"
}

variable "ssh_keys" {
  description = "List of SSH key names or IDs to add to the server"
  type        = list(string)
  default     = []
}

variable "docker_compose_dir" {
  description = "Path to docker-compose.yml and .env on the server"
  type        = string
  default     = "/opt/kendo"
}
