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

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"; }
kube() { kubectl --context "$CONTEXT" --namespace "$NAMESPACE" "$@"; }
kind_local() {
    if [[ -x "$ROOT/.tools/kind" ]]; then "$ROOT/.tools/kind" "$@";
    else need kind; kind "$@"; fi
}

help_text() {
    cat <<'HELP'
Usage: ./k8s.sh COMMAND [TARGET]

  up                        Create/reuse local Kind; build/load all; infra, migrate, rollout
  deploy [listings|rabbitmq|all]
                            Rebuild/load and update an existing local cluster (default: listings)
  build [TARGET]            Build images only (default: all)
  load [TARGET]             Load existing Docker images into Kind (default: all)
  migrate                   Build/load latest EF bundle; recreate finished Job; wait and show logs
  migration-add NAME        Generate a local Listings EF migration; no database connection
  restart [TARGET]          Restart api/worker/listings/rabbitmq/all (default: listings)
  stop                      Stop API, worker, RabbitMQ and PostgreSQL; retain PVCs
  status                    Show local workloads, Services, Jobs and PVCs
  logs [TARGET] [--follow]   Show api/worker/db/rabbitmq/migrations logs (default: worker)
  ui                        Forward RabbitMQ management to http://localhost:15672; Ctrl+C stops
  config                    Create missing ignored .env files from examples; edit placeholders
  validate                  Render/check the local Kustomize overlay without applying it
  help                      Show this help

Build/load targets: api, worker, migrations, rabbitmq, listings (= first three), all.
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
    for file in "$KUBE_DIR/local/.env" "$KUBE_DIR/local/rabbitmq/.env"; do
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
        listings-credentials) keys='postgres-password connection-string' ;;
        rabbitmq-credentials) keys='username password erlang-cookie' ;;
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
        api|worker|migrations|rabbitmq) printf '%s\n' "$1" ;;
        listings) printf '%s\n' api worker migrations ;;
        all) printf '%s\n' api worker migrations rabbitmq ;;
        *) fail "Unknown image target '$1'. Use api, worker, migrations, rabbitmq, listings or all." ;;
    esac
}

build_images() {
    local selected target dockerfile
    selected=$(targets "$1")
    need docker
    while IFS= read -r target; do
        case "$target" in
            api) dockerfile=$API_DOCKERFILE ;;
            migrations)
                docker build --provenance=false --file "$API_DOCKERFILE" --target migrations --tag smartlost-listings-migrations:local .
                continue ;;
            worker) dockerfile=src/Services/Listings/SmartLost.ListingsService.Worker/Dockerfile ;;
            rabbitmq) dockerfile=$KUBE_DIR/images/rabbitmq/Dockerfile ;;
        esac
        local image="smartlost-listings-$target:local"
        [[ "$target" != rabbitmq ]] || image=smartlost-rabbitmq:local
        docker build --provenance=false --file "$dockerfile" --tag "$image" .
    done <<< "$selected"
}

load_images() {
    local selected target image
    selected=$(targets "$1")
    while IFS= read -r target; do
        image="smartlost-listings-$target:local"
        [[ "$target" != rabbitmq ]] || image=smartlost-rabbitmq:local
        kind_local load docker-image "$image" --name "$CLUSTER"
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

start_infrastructure() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret listings-credentials "$KUBE_DIR/local/.env"
    ensure_secret rabbitmq-credentials "$KUBE_DIR/local/rabbitmq/.env"
    kube apply -f "$KUBE_DIR/base/listings/db-service.yaml" -f "$KUBE_DIR/base/listings/db-statefulset.yaml"
    kube rollout status statefulset/listings-db --timeout=180s
    start_broker "$1"
}

run_migration() {
    local previous name complete failed
    previous=$(kube get job listings-migrate --ignore-not-found -o 'jsonpath={.metadata.name}|{.status.conditions[?(@.type=="Complete")].status}|{.status.conditions[?(@.type=="Failed")].status}')
    IFS='|' read -r name complete failed <<< "$previous"
    if [[ -n "$name" ]]; then
        if [[ "$complete" != True && "$failed" != True ]]; then
            fail "listings-migrate is still running. Use ./k8s.sh logs migrations; it will not be replaced."
        fi
        kube delete job listings-migrate --wait=true
    fi
    kube apply -f "$KUBE_DIR/migrations/listings-job.yaml"
    if ! kube wait --for=condition=complete job/listings-migrate --timeout=300s; then
        kube logs job/listings-migrate --tail=100 || true
        fail "Migration did not complete. Application rollout was not started; inspect the retained Job."
    fi
    kube logs job/listings-migrate --tail=100
}

restart_target() {
    case "$1" in
        api) kube rollout restart deployment/listings-service; kube rollout status deployment/listings-service --timeout=180s ;;
        worker) kube rollout restart deployment/listings-outbox-worker; kube rollout status deployment/listings-outbox-worker --timeout=180s ;;
        rabbitmq) kube rollout restart statefulset/rabbitmq; kube rollout status statefulset/rabbitmq --timeout=300s ;;
        listings) restart_target api; restart_target worker ;;
        all) restart_target rabbitmq; restart_target listings ;;
        *) fail "Unknown restart target '$1'. Use api, worker, listings, rabbitmq or all." ;;
    esac
}

deploy_target() {
    local target=$1
    case "$target" in listings|rabbitmq|all) ;; *) fail "Deploy target must be listings, rabbitmq or all." ;; esac
    check_cluster
    build_images "$target"
    load_images "$target"
    if [[ "$target" == rabbitmq ]]; then
        kube apply -f "$KUBE_DIR/base/namespace.yaml"
        ensure_secret rabbitmq-credentials "$KUBE_DIR/local/rabbitmq/.env"
        start_broker yes
        return
    fi
    local refresh_broker=no
    [[ "$target" != all ]] || refresh_broker=yes
    start_infrastructure "$refresh_broker"
    run_migration
    kube apply -k "$KUBE_DIR/overlays/local"
    # Local tags and subPath ConfigMap mounts require replacing existing pods.
    restart_target listings
    printf 'Listings Swagger: http://localhost:5082/swagger\nRabbitMQ UI: ./k8s.sh ui\n'
}

logs_target() {
    local resource
    case "$1" in
        api) resource=deployment/listings-service ;;
        worker) resource=deployment/listings-outbox-worker ;;
        db) resource=statefulset/listings-db ;;
        rabbitmq) resource=statefulset/rabbitmq ;;
        migrations) resource=job/listings-migrate ;;
        *) fail "Unknown log target '$1'. Use api, worker, db, rabbitmq or migrations." ;;
    esac
    case "$2" in
        '') kube logs "$resource" --tail=100 ;;
        --follow) kube logs "$resource" --tail=100 --follow ;;
        *) fail "Logs only accepts the optional --follow flag." ;;
    esac
}

command_name=${1:-help}
[[ $# -eq 0 ]] || shift
if [[ "$command_name" == logs ]]; then
    [[ $# -le 2 ]] || fail 'Usage: ./k8s.sh logs [TARGET] [--follow]'
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
        [[ $# -eq 0 ]] || fail 'migrate takes no arguments.'
        check_cluster; build_images migrations; load_images migrations
        kube rollout status statefulset/listings-db --timeout=180s
        run_migration ;;
    migration-add)
        [[ $# -eq 1 && "$1" =~ ^[A-Za-z][A-Za-z0-9_]*$ ]] || fail 'Provide a migration name, e.g. ./k8s.sh migration-add AddListingLocation'
        need dotnet; dotnet tool restore
        dotnet ef migrations add "$1" --project "$INFRA_PROJECT" --startup-project "$INFRA_PROJECT" --context ListingsDbContext --output-dir Persistence/Migrations ;;
    restart) check_cluster; restart_target "${1:-listings}" ;;
    stop)
        [[ $# -eq 0 ]] || fail 'stop takes no arguments.'
        check_cluster
        kube scale deployment/listings-service deployment/listings-outbox-worker --replicas=0
        kube wait --for=delete pod -l app.kubernetes.io/name=listings-service --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=listings-outbox-worker --timeout=180s
        kube scale statefulset/rabbitmq statefulset/listings-db --replicas=0 ;;
    status)
        [[ $# -eq 0 ]] || fail 'status takes no arguments.'
        check_cluster; kube get deployments,statefulsets,pods,services,jobs,pvc ;;
    logs) check_cluster; logs_target "${1:-worker}" "${2:-}" ;;
    ui)
        [[ $# -eq 0 ]] || fail 'ui takes no arguments.'
        check_cluster
        printf 'RabbitMQ: http://localhost:15672 (credentials in deploy/kubernetes/local/rabbitmq/.env). Ctrl+C stops forwarding.\n'
        kube port-forward service/rabbitmq 15672:15672 ;;
    validate)
        [[ $# -eq 0 ]] || fail 'validate takes no arguments.'
        need kubectl; kubectl kustomize "$KUBE_DIR/overlays/local" >/dev/null
        printf 'Local Kustomize overlay rendered successfully; no cluster changes applied.\n' ;;
    *) fail "Unknown command '$command_name'. Run ./k8s.sh help." ;;
esac
