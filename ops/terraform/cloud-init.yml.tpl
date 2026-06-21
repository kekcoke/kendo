#cloud-config
package_update: true

packages:
  - docker-compose-plugin

runcmd:
  - mkdir -p ${compose_dir}
  - cd ${compose_dir}
