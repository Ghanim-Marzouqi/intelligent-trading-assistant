#!/bin/bash
# Daily PostgreSQL backup script
# Add to crontab: 0 2 * * * /path/to/backup.sh

set -euo pipefail

BACKUP_DIR="/backups/postgres"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/trading_assistant_${TIMESTAMP}.sql.gz"
RETENTION_DAYS=7

# Create backup directory if it doesn't exist
mkdir -p "${BACKUP_DIR}"

# Run pg_dump inside the postgres container and compress
docker compose exec -T postgres pg_dump -U "${DB_USER}" trading_assistant | gzip > "${BACKUP_FILE}"

# Verify backup was created
if [[ -f "${BACKUP_FILE}" ]]; then
    echo "Backup created: ${BACKUP_FILE}"
    echo "Size: $(du -h "${BACKUP_FILE}" | cut -f1)"
else
    echo "ERROR: Backup failed!"
    exit 1
fi

# Remove backups older than retention period
find "${BACKUP_DIR}" -name "trading_assistant_*.sql.gz" -mtime +${RETENTION_DAYS} -delete
echo "Cleaned up backups older than ${RETENTION_DAYS} days"
