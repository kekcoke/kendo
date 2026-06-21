# Kendo — Hetzner Cloud Server
# Provisions a single VM with Docker Compose for Kendo platform

resource "hcloud_server" "kendo" {
  name        = var.server_name
  server_type = var.server_type
  image       = "docker-ce"
  location    = var.location
  ssh_keys    = var.ssh_keys

  # Upload docker-compose.yml and .env on first boot
  user_data = templatefile("${path.module}/cloud-init.yml", {
    compose_dir = var.docker_compose_dir
  })

  labels = {
    project = "kendo"
    phase   = "08"
    managed = "terraform"
  }
}

resource "hcloud_firewall" "kendo" {
  name = "kendo-firewall"

  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "22"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "80"
    source_ips = ["0.0.0.0/0", "::/0"]
  }

  rule {
    direction = "in"
    protocol  = "tcp"
    port      = "443"
    source_ips = ["0.0.0.0/0", "::/0"]
  }
}

resource "hcloud_firewall_attachment" "kendo" {
  firewall_id = hcloud_firewall.kendo.id
  server_ids  = [hcloud_server.kendo.id]
}

output "server_ipv4" {
  value = hcloud_server.kendo.ipv4_address
  description = "Public IPv4 address of the Kendo server"
}

output "server_ipv6" {
  value = hcloud_server.kendo.ipv6_address
  description = "Public IPv6 address of the Kendo server"
}
