#!/usr/bin/env bash
set -euo pipefail

file="api/features/bio-idn-006/RoleAssignmentService.cs"
consume_line="$(grep -n 'RequireSuperAdminMutationAuthorityAsync(tenantId, actor, SuperAdminAssignActionKey' "$file" | cut -d: -f1)"
existence_line="$(grep -n 'personGateway.ExistsAsync(personId' "$file" | head -1 | cut -d: -f1)"

echo "TryConsumeApprovalAsync call line: $consume_line"
echo "target existence check line: $existence_line"
if (( consume_line < existence_line )); then
  echo "REPRODUCED: approval is consumed before target existence validation; a failed target mutation can burn a valid approval."
  exit 1
fi
echo "NOT REPRODUCED"
