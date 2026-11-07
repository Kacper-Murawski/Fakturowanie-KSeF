#!/usr/bin/env bash
# Kopia zapasowa bazy (D48) - uruchamiana np. codziennie z crona na serwerze:
#   15 2 * * * /opt/fakturowanie/deploy/backup-db.sh >> /var/log/fakturowanie-backup.log 2>&1
# Przechowuje kopie z ostatnich 14 dni. Kopię warto dodatkowo przenosić poza serwer.
# UWAGA: tokeny KSeF w kopii są zaszyfrowane - do odtworzenia potrzebny jest także certyfikat z deploy/secrets/.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p backups
file="backups/ksef-$(date +%Y%m%d-%H%M%S).sql.gz"
docker compose -f docker-compose.prod.yml --env-file .env exec -T db pg_dump -U ksef -d ksef --no-owner | gzip > "$file"
find backups -name 'ksef-*.sql.gz' -mtime +14 -delete
echo "$(date -Is) kopia zapisana: $file"
