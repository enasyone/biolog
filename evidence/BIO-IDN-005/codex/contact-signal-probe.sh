#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../../.." && pwd)"
gateway="$root/api/features/bio-idn-005/Domain/IPersonAttributeGateway.cs"
signals="$root/api/features/bio-idn-005/Domain/DuplicateSignalSource.cs"
service="$root/api/features/bio-idn-005/DuplicateReviewService.cs"

if grep -Eqi '\b(email|phone|telephone|address|contact(point|data)?)\b' "$gateway" \
  && grep -Eqi '\b(contactdata|contactpoint|email|phone|address)\b' "$signals" \
  && grep -Eqi '\b(contactdata|contactpoint|email|phone|address)\b' "$service"; then
  echo "PASS: contact data can independently raise a duplicate candidate"
  exit 0
fi

echo "FAIL: duplicate detection has no contact-data field or signal; name and date of birth are identity attributes, not contact data" >&2
exit 1
