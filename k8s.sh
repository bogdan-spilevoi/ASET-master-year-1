#!/usr/bin/env bash
# Local developer commands only. GitHub Actions keeps its own inline orchestration.
set -euo pipefail

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$ROOT"
CLUSTER=smartlost-local
CONTEXT=kind-smartlost-local
NAMESPACE=smartlost
KUBE_DIR=deploy/kubernetes
API_DOCKERFILE=src/Services/Listings/SmartLost.ListingsService.Api/Dockerfile
INFRA_PROJECT=src/Services/Listings/SmartLost.ListingsService.Infrastructure
MATCHING_DOCKERFILE=src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile
MATCHING_INFRA_PROJECT=src/Services/Matching/SmartLost.MatchingService.Infrastructure
MATCHING_LOCAL_PORT=5083
# Rendered overlay objects carry this metadata label only when they belong to Matching.
MATCHING_SELECTOR=smartlost/service=matching
# Listings rollouts skip Matching and the shared Redis cache, which have their own apply steps.
LISTINGS_SELECTOR='smartlost/service notin (matching,redis)'

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"; }
kube() { kubectl --context "$CONTEXT" --namespace "$NAMESPACE" "$@"; }
exists() { [[ -n "$(kube get "$1" --ignore-not-found -o name)" ]]; }
kind_local() {
    if [[ -x "$ROOT/.tools/kind" ]]; then "$ROOT/.tools/kind" "$@";
    else need kind; kind "$@"; fi
}

help_text() {
    cat <<'HELP'
Usage: ./k8s.sh COMMAND [TARGET]

  up                        Create/reuse local Kind; build/load all; infra, migrate, rollout
  deploy [listings|matching|rabbitmq|all]
                            Rebuild/load and update an existing local cluster (default: listings)
  build [TARGET]            Build images only (default: all)
  load [TARGET]             Load existing Docker images into Kind (default: all)
  migrate [listings|matching]
                            Build/load latest EF bundle; recreate finished Job; wait and show logs
  migration-add NAME [listings|matching]
                            Generate a local EF migration (default: listings); no database connection
  restart [TARGET]          Restart api/worker/listings/matching/redis/rabbitmq/all (default: listings)
  stop                      Stop deployed APIs, worker, RabbitMQ, PostgreSQL and Redis; retain PVCs
  status                    Show local workloads, Services, Jobs and PVCs
  logs [TARGET] [--follow]  Show api/worker/db/rabbitmq/migrations or
                            matching-api/matching-db/matching-migrations/redis logs (default: worker)
  ui [rabbitmq|matching]    Forward RabbitMQ management to http://localhost:15672 (default) or
                            the Matching API to http://localhost:5083; Ctrl+C stops
  config                    Create missing ignored .env files from examples; edit placeholders
  validate                  Render/check the local Kustomize overlay without applying it
  help                      Show this help

Build/load targets: api, worker, migrations, rabbitmq, listings (= first three),
matching-api, matching-migrations, matching (= both Matching images), all.
All cluster commands explicitly use kind-smartlost-local / namespace smartlost.
No command deletes database volumes, the namespace, or the cluster. No seed runs automatically.
HELP
}

check_cluster() {
    need kubectl
    kube get namespace default >/dev/null
}

create_cluster() {
    need docker; need kubectl
    docker info >/dev/null
    local clusters
    clusters=$(kind_local get clusters)
    if ! printf '%s\n' "$clusters" | grep -Fxq "$CLUSTER"; then
        kind_local create cluster --name "$CLUSTER" --config "$KUBE_DIR/local/kind.yaml" --wait 120s
    fi
    check_cluster
}

config_files() {
    local file
    for file in "$KUBE_DIR/local/.env" "$KUBE_DIR/local/rabbitmq/.env" "$KUBE_DIR/local/matching/.env" "$KUBE_DIR/local/redis/.env"; do
        if [[ ! -e "$file" ]]; then
            (umask 077; cp "$file.example" "$file")
            printf 'Created %s; replace its placeholders before first startup.\n' "$file"
        else
            printf 'Keeping existing %s\n' "$file"
        fi
    done
}

ensure_secret() {
    local name=$1 file=$2 existing keys key
    existing=$(kube get secret "$name" --ignore-not-found -o name)
    [[ -z "$existing" ]] || return 0
    [[ -f "$file" ]] || fail "Missing $file. Run ./k8s.sh config and edit the placeholders."
    if grep -q 'replace-with-' "$file"; then
        fail "Replace the placeholders in $file before creating $name."
    fi
    case "$name" in
        listings-credentials|matching-credentials) keys='postgres-password connection-string' ;;
        rabbitmq-credentials) keys='username password erlang-cookie' ;;
        redis-credentials) keys='password connection-string' ;;
        *) fail "No local credential schema is configured for $name." ;;
    esac
    for key in $keys; do
        grep -q "^$key=." "$file" || fail "Missing or empty '$key' in $file."
    done
    # Do not source .env as shell code or print the generated Secret.
    kube create secret generic "$name" --from-env-file="$file" --dry-run=client -o yaml |
        kube apply -f -
}

targets() {
    case "$1" in
        api|worker|migrations|rabbitmq|matching-api|matching-migrations) printf '%s\n' "$1" ;;
        listings) printf '%s\n' api worker migrations ;;
        matching) printf '%s\n' matching-api matching-migrations ;;
        all) printf '%s\n' api worker migrations rabbitmq matching-api matching-migrations ;;
        *) fail "Unknown image target '$1'. Use api, worker, migrations, rabbitmq, listings, matching-api, matching-migrations, matching or all." ;;
    esac
}

image_name() {
    case "$1" in
        rabbitmq) printf 'smartlost-rabbitmq:local\n' ;;
        matching-*) printf 'smartlost-%s:local\n' "$1" ;;
        *) printf 'smartlost-listings-%s:local\n' "$1" ;;
    esac
}

build_images() {
    local selected target dockerfile image
    selected=$(targets "$1")
    need docker
    while IFS= read -r target; do
        image=$(image_name "$target")
        case "$target" in
            api) dockerfile=$API_DOCKERFILE ;;
            migrations)
                docker build --provenance=false --file "$API_DOCKERFILE" --target migrations --tag "$image" .
                continue ;;
            worker) dockerfile=src/Services/Listings/SmartLost.ListingsService.Worker/Dockerfile ;;
            rabbitmq) dockerfile=$KUBE_DIR/images/rabbitmq/Dockerfile ;;
            matching-api) dockerfile=$MATCHING_DOCKERFILE ;;
            matching-migrations)
                docker build --provenance=false --file "$MATCHING_DOCKERFILE" --target migrations --tag "$image" .
                continue ;;
        esac
        docker build --provenance=false --file "$dockerfile" --tag "$image" .
    done <<< "$selected"
}

load_images() {
    local selected target
    selected=$(targets "$1")
    while IFS= read -r target; do
        kind_local load docker-image "$(image_name "$target")" --name "$CLUSTER"
    done <<< "$selected"
}

start_broker() {
    local restart=$1 existing
    existing=$(kube get statefulset rabbitmq --ignore-not-found -o name)
    kube apply -k "$KUBE_DIR/base/rabbitmq"
    if [[ "$restart" == yes && -n "$existing" ]]; then
        kube rollout restart statefulset/rabbitmq
    fi
    kube rollout status statefulset/rabbitmq --timeout=300s
}

# Shared by Listings and Matching; both deploy paths call this, so either can run alone.
start_redis() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret redis-credentials "$KUBE_DIR/local/redis/.env"
    kube apply -k "$KUBE_DIR/base/redis"
    kube rollout status deployment/redis --timeout=180s
}

start_infrastructure() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret listings-credentials "$KUBE_DIR/local/.env"
    ensure_secret rabbitmq-credentials "$KUBE_DIR/local/rabbitmq/.env"
    start_redis
    kube apply -f "$KUBE_DIR/base/listings/db-service.yaml" -f "$KUBE_DIR/base/listings/db-statefulset.yaml"
    kube rollout status statefulset/listings-db --timeout=180s
    start_broker "$1"
}

start_matching_database() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret matching-credentials "$KUBE_DIR/local/matching/.env"
    kube apply -f "$KUBE_DIR/base/matching/db-service.yaml" -f "$KUBE_DIR/base/matching/db-statefulset.yaml"
    kube rollout status statefulset/matching-db --timeout=180s
}

run_migration() {
    local service=${1:-listings} job manifest log_target previous name complete failed
    case "$service" in
        listings) job=listings-migrate; manifest=$KUBE_DIR/migrations/listings-job.yaml; log_target=migrations ;;
        matching) job=matching-migrate; manifest=$KUBE_DIR/migrations/matching-job.yaml; log_target=matching-migrations ;;
        *) fail "Migration target must be listings or matching." ;;
    esac
    previous=$(kube get job "$job" --ignore-not-found -o 'jsonpath={.metadata.name}|{.status.conditions[?(@.type=="Complete")].status}|{.status.conditions[?(@.type=="Failed")].status}')
    IFS='|' read -r name complete failed <<< "$previous"
    if [[ -n "$name" ]]; then
        if [[ "$complete" != True && "$failed" != True ]]; then
            fail "$job is still running. Use ./k8s.sh logs $log_target; it will not be replaced."
        fi
        kube delete job "$job" --wait=true
    fi
    kube apply -f "$manifest"
    if ! kube wait --for=condition=complete "job/$job" --timeout=300s; then
        kube logs "job/$job" --tail=100 || true
        fail "Migration did not complete. Application rollout was not started; inspect the retained Job."
    fi
    kube logs "job/$job" --tail=100
}

restart_target() {
    case "$1" in
        api) kube rollout restart deployment/listings-service; kube rollout status deployment/listings-service --timeout=180s ;;
        worker) kube rollout restart deployment/listings-outbox-worker; kube rollout status deployment/listings-outbox-worker --timeout=180s ;;
        rabbitmq) kube rollout restart statefulset/rabbitmq; kube rollout status statefulset/rabbitmq --timeout=300s ;;
        listings) restart_target api; restart_target worker ;;
        matching) kube rollout restart deployment/matching-service; kube rollout status deployment/matching-service --timeout=180s ;;
        redis) kube rollout restart deployment/redis; kube rollout status deployment/redis --timeout=180s ;;
        all)
            restart_target rabbitmq; restart_target listings
            if exists deployment/matching-service; then restart_target matching; fi ;;
        *) fail "Unknown restart target '$1'. Use api, worker, listings, matching, redis, rabbitmq or all." ;;
    esac
}

deploy_matching() {
    start_redis
    start_matching_database
    run_migration matching
    # Applied only after the migration Job succeeded; Listings objects are not touched.
    kube apply -k "$KUBE_DIR/overlays/local" --selector "$MATCHING_SELECTOR"
    # Local tags and subPath ConfigMap mounts require replacing existing pods.
    restart_target matching
}

deploy_target() {
    local target=$1
    case "$target" in listings|matching|rabbitmq|all) ;; *) fail "Deploy target must be listings, matching, rabbitmq or all." ;; esac
    check_cluster
    build_images "$target"
    load_images "$target"
    if [[ "$target" == rabbitmq ]]; then
        kube apply -f "$KUBE_DIR/base/namespace.yaml"
        ensure_secret rabbitmq-credentials "$KUBE_DIR/local/rabbitmq/.env"
        start_broker yes
        return
    fi
    if [[ "$target" == matching ]]; then
        deploy_matching
        printf 'Matching Swagger: ./k8s.sh ui matching, then http://localhost:%s/swagger\n' "$MATCHING_LOCAL_PORT"
        return
    fi
    local refresh_broker=no
    [[ "$target" != all ]] || refresh_broker=yes
    start_infrastructure "$refresh_broker"
    run_migration listings
    kube apply -k "$KUBE_DIR/overlays/local" --selector "$LISTINGS_SELECTOR"
    # Local tags and subPath ConfigMap mounts require replacing existing pods.
    restart_target listings
    if [[ "$target" == all ]]; then
        deploy_matching
    fi
    printf 'Listings Swagger: http://localhost:5082/swagger\nRabbitMQ UI: ./k8s.sh ui\n'
    if [[ "$target" == all ]]; then
        printf 'Matching Swagger: ./k8s.sh ui matching, then http://localhost:%s/swagger\n' "$MATCHING_LOCAL_PORT"
    fi
}

logs_target() {
    local resource
    case "$1" in
        api) resource=deployment/listings-service ;;
        worker) resource=deployment/listings-outbox-worker ;;
        db) resource=statefulset/listings-db ;;
        rabbitmq) resource=statefulset/rabbitmq ;;
        migrations) resource=job/listings-migrate ;;
        matching-api) resource=deployment/matching-service ;;
        matching-db) resource=statefulset/matching-db ;;
        matching-migrations) resource=job/matching-migrate ;;
        redis) resource=deployment/redis ;;
        *) fail "Unknown log target '$1'. Use api, worker, db, rabbitmq, migrations, matching-api, matching-db, matching-migrations or redis." ;;
    esac
    case "$2" in
        '') kube logs "$resource" --tail=100 ;;
        --follow) kube logs "$resource" --tail=100 --follow ;;
        *) fail "Logs only accepts the optional --follow flag." ;;
    esac
}

stop_workloads() {
    local stopped=no
    if exists deployment/matching-service; then
        kube scale deployment/matching-service --replicas=0
        kube wait --for=delete pod -l app.kubernetes.io/name=matching-service --timeout=180s
        stopped=yes
    fi
    if exists statefulset/matching-db; then
        kube scale statefulset/matching-db --replicas=0
        stopped=yes
    fi
    if exists deployment/listings-service; then
        kube scale deployment/listings-service deployment/listings-outbox-worker --replicas=0
        kube wait --for=delete pod -l app.kubernetes.io/name=listings-service --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=listings-outbox-worker --timeout=180s
        kube scale statefulset/rabbitmq statefulset/listings-db --replicas=0
        stopped=yes
    fi
    # Stop the cache last; its contents are disposable and not persisted.
    if exists deployment/redis; then
        kube scale deployment/redis --replicas=0
        stopped=yes
    fi
    [[ "$stopped" == yes ]] || printf 'No Listings or Matching workloads exist; nothing to stop.\n'
}

command_name=${1:-help}
[[ $# -eq 0 ]] || shift
if [[ "$command_name" == logs || "$command_name" == migration-add ]]; then
    [[ $# -le 2 ]] || fail "Too many arguments for $command_name. Run ./k8s.sh help."
else
    [[ $# -le 1 ]] || fail 'Too many arguments. Run ./k8s.sh help.'
fi
case "$command_name" in
    help|-h|--help) help_text ;;
    config) [[ $# -eq 0 ]] || fail 'config takes no arguments.'; config_files ;;
    up) [[ $# -eq 0 ]] || fail 'up takes no arguments.'; create_cluster; deploy_target all ;;
    deploy) deploy_target "${1:-listings}" ;;
    build) build_images "${1:-all}" ;;
    load) check_cluster; load_images "${1:-all}" ;;
    migrate)
        case "${1:-listings}" in
            listings)
                check_cluster; build_images migrations; load_images migrations
                kube rollout status statefulset/listings-db --timeout=180s
                run_migration listings ;;
            matching)
                check_cluster; build_images matching-migrations; load_images matching-migrations
                kube rollout status statefulset/matching-db --timeout=180s
                run_migration matching ;;
            *) fail 'Migration target must be listings or matching.' ;;
        esac ;;
    migration-add)
        [[ $# -ge 1 && "$1" =~ ^[A-Za-z][A-Za-z0-9_]*$ ]] || fail 'Provide a migration name, e.g. ./k8s.sh migration-add AddListingLocation'
        need dotnet; dotnet tool restore
        case "${2:-listings}" in
            listings) dotnet ef migrations add "$1" --project "$INFRA_PROJECT" --startup-project "$INFRA_PROJECT" --context ListingsDbContext --output-dir Persistence/Migrations ;;
            matching) dotnet ef migrations add "$1" --project "$MATCHING_INFRA_PROJECT" --startup-project "$MATCHING_INFRA_PROJECT" --context MatchingDbContext --output-dir Persistence/Migrations ;;
            *) fail 'Migration project must be listings or matching.' ;;
        esac ;;
    restart) check_cluster; restart_target "${1:-listings}" ;;
    stop)
        [[ $# -eq 0 ]] || fail 'stop takes no arguments.'
        check_cluster
        stop_workloads ;;
    status)
        [[ $# -eq 0 ]] || fail 'status takes no arguments.'
        check_cluster; kube get deployments,statefulsets,pods,services,jobs,pvc ;;
    logs) check_cluster; logs_target "${1:-worker}" "${2:-}" ;;
    ui)
        check_cluster
        case "${1:-rabbitmq}" in
            rabbitmq)
                printf 'RabbitMQ: http://localhost:15672 (credentials in deploy/kubernetes/local/rabbitmq/.env). Ctrl+C stops forwarding.\n'
                kube port-forward service/rabbitmq 15672:15672 ;;
            matching)
                printf 'Matching Swagger: http://localhost:%s/swagger (Development-only routes). Ctrl+C stops forwarding.\n' "$MATCHING_LOCAL_PORT"
                kube port-forward service/matching-service "$MATCHING_LOCAL_PORT:80" ;;
            *) fail 'UI target must be rabbitmq or matching.' ;;
        esac ;;
    validate)
        [[ $# -eq 0 ]] || fail 'validate takes no arguments.'
        need kubectl; kubectl kustomize "$KUBE_DIR/overlays/local" >/dev/null
        printf 'Local Kustomize overlay rendered successfully; no cluster changes applied.\n' ;;
    *) fail "Unknown command '$command_name'. Run ./k8s.sh help." ;;
esac
