#!/bin/sh
# Populate an already-migrated local Listings database, then exit without serving HTTP.
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_dir=$(CDPATH= cd -- "$script_dir/../../.." && pwd)
cd "$repository_dir"

exec dotnet run \
  --project "$script_dir/SmartLost.ListingsService.Api" \
  --configuration Release \
  --no-launch-profile \
  -- --environment Development --SeedData:Only=true
