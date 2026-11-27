#!/usr/bin/env bash
# Local developer commands only. GitHub Actions keeps its own inline orchestration.
set -euo pipefail

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$ROOT"
CLUSTER=smartlost-local
CONTEXT=kind-smartlost-local
NAMESPACE=smartlost
KUBE_DIR=deploy/kubernetes
LISTINGS_API_DOCKERFILE=src/Services/Listings/SmartLost.ListingsService.Api/Dockerfile
LISTINGS_WORKER_DOCKERFILE=src/Services/Listings/SmartLost.ListingsService.Worker/Dockerfile
LISTINGS_INFRA_PROJECT=src/Services/Listings/SmartLost.ListingsService.Infrastructure
AUTH_API_DOCKERFILE=src/Services/Auth/SmartLost.AuthService.Api/Dockerfile
AUTH_WORKER_DOCKERFILE=src/Services/Auth/SmartLost.AuthService.Worker/Dockerfile
MATCHING_DOCKERFILE=src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile
MATCHING_INFRA_PROJECT=src/Services/Matching/SmartLost.MatchingService.Infrastructure
MATCHING_LOCAL_PORT=5083
AUTH_INFRA_PROJECT=src/Services/Auth/SmartLost.AuthService.Infrastructure

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
  deploy [web|listings|auth|matching|rabbitmq|mailpit|all]
                            Rebuild/load and update an existing local cluster (default: all)
  build [TARGET]            Build images only (default: all)
  load [TARGET]             Load existing Docker images into Kind (default: all)
  migrate [listings|auth|matching|all]
                            Build/load EF bundle(s); recreate finished Job(s); wait and show logs
  migration-add [SERVICE] NAME
                            Generate a local EF migration (SERVICE: listings, auth or matching, default: listings)
  restart [TARGET]          Restart web/auth-api/auth-worker/auth-identity/listings-api/listings-worker/auth/listings/matching/mailpit/rabbitmq/redis/all
  stop                      Stop workloads, Mailpit, Redis, RabbitMQ and PostgreSQL; retain PVCs
  status                    Show local workloads, Services, Jobs and PVCs
  logs [TARGET] [--follow]  Show web/api/worker/db/rabbitmq/mailpit/auth-api/auth-worker/auth-identity/auth-db/matching-api/matching-db/matching-migrations/redis/migrations logs
  ui [web|rabbitmq|mailpit|matching] Forward SmartLost (4200), RabbitMQ (15672), Mailpit (8025) or Matching (5083) UI; Ctrl+C stops (default: rabbitmq)
  config                    Create missing ignored .env files from examples; edit placeholders
  validate                  Render/check the local Kustomize overlay without applying it
  help                      Show this help

Build/load targets: listings-api (api), listings-worker (worker), listings-migrations (migrations),
auth-api, auth-worker, auth-migrations, matching-api, matching-migrations, rabbitmq, listings, matching, auth, web, all.
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
    for file in "$KUBE_DIR/local/.env" "$KUBE_DIR/local/rabbitmq/.env" "$KUBE_DIR/local/auth/.env" "$KUBE_DIR/local/matching/.env" "$KUBE_DIR/local/redis/.env"; do
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
        redis-credentials) keys='password connection-string' ;;
        listings-credentials|matching-credentials) keys='postgres-password connection-string' ;;
        rabbitmq-credentials) keys='username password erlang-cookie' ;;
        auth-credentials) keys='postgres-password connection-string jwt-signing-key' ;;
        *) fail "No local credential schema is configured for $name." ;;
    esac
    for key in $keys; do
        grep -q "^$key=." "$file" || fail "Missing or empty '$key' in $file."
    done
    if [[ "$name" == auth-credentials ]]; then
        grep -q '^jwt-signing-key=.\{32,\}' "$file" || fail "jwt-signing-key in $file must be at least 32 characters."
    fi
    # Do not source .env as shell code or print the generated Secret.
    kube create secret generic "$name" --from-env-file="$file" --dry-run=client -o yaml |
        kube apply -f -
}

targets() {
    case "$1" in
        api|listings-api) printf '%s\n' listings-api ;;
        worker|listings-worker) printf '%s\n' listings-worker ;;
        migrations|listings-migrations) printf '%s\n' listings-migrations ;;
        auth-api) printf '%s\n' auth-api ;;
        auth-worker) printf '%s\n' auth-worker ;;
        web|auth-migrations|matching-api|matching-migrations) printf '%s\n' "$1" ;;
        matching) printf '%s\n' matching-api matching-migrations ;;
        rabbitmq) printf '%s\n' rabbitmq ;;
        listings) printf '%s\n' listings-api listings-worker listings-migrations ;;
        auth) printf '%s\n' auth-api auth-worker auth-migrations ;;
        all) printf '%s\n' listings-api listings-worker listings-migrations auth-api auth-worker auth-migrations matching-api matching-migrations rabbitmq web ;;
        *) fail "Unknown image target '$1'. Use listings-api, listings-worker, listings-migrations, auth-api, auth-worker, auth-migrations, matching-api, matching-migrations, rabbitmq, web, listings, matching, auth or all." ;;
    esac
}

image_for_target() {
    case "$1" in
        listings-api) printf '%s\n' smartlost-listings-api:local ;;
        listings-worker) printf '%s\n' smartlost-listings-worker:local ;;
        listings-migrations) printf '%s\n' smartlost-listings-migrations:local ;;
        auth-api) printf '%s\n' smartlost-auth-api:local ;;
        auth-worker) printf '%s\n' smartlost-auth-worker:local ;;
        auth-migrations) printf '%s\n' smartlost-auth-migrations:local ;;
        web|matching-api|matching-migrations) printf 'smartlost-%s:local\n' "$1" ;;
        rabbitmq) printf '%s\n' smartlost-rabbitmq:local ;;
        *) fail "Unknown image target '$1'." ;;
    esac
}

build_images() {
    local selected target image
    selected=$(targets "$1")
    need docker
    while IFS= read -r target; do
        image=$(image_for_target "$target")
        case "$target" in
            web)
                docker build --provenance=false --tag "$image" frontend ;;
            listings-api)
                docker build --provenance=false --file "$LISTINGS_API_DOCKERFILE" --tag "$image" . ;;
            listings-migrations)
                docker build --provenance=false --file "$LISTINGS_API_DOCKERFILE" --target migrations --tag "$image" . ;;
            listings-worker)
                docker build --provenance=false --file "$LISTINGS_WORKER_DOCKERFILE" --tag "$image" . ;;
            matching-api)
                docker build --provenance=false --file "$MATCHING_DOCKERFILE" --tag "$image" . ;;
            matching-migrations)
                docker build --provenance=false --file "$MATCHING_DOCKERFILE" --target migrations --tag "$image" . ;;
            auth-api)
                docker build --provenance=false --file "$AUTH_API_DOCKERFILE" --tag "$image" . ;;
            auth-worker)
                docker build --provenance=false --file "$AUTH_WORKER_DOCKERFILE" --tag "$image" . ;;
            auth-migrations)
                docker build --provenance=false --file "$AUTH_API_DOCKERFILE" --target migrations --tag "$image" . ;;
            rabbitmq)
                docker build --provenance=false --file "$KUBE_DIR/images/rabbitmq/Dockerfile" --tag "$image" . ;;
        esac
    done <<< "$selected"
}

load_images() {
    local selected target image
    selected=$(targets "$1")
    while IFS= read -r target; do
        image=$(image_for_target "$target")
        kind_local load docker-image "$image" --name "$CLUSTER"
    done <<< "$selected"
}

start_broker() {
    local restart=$1 existing
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret rabbitmq-credentials "$KUBE_DIR/local/rabbitmq/.env"
    existing=$(kube get statefulset rabbitmq --ignore-not-found -o name)
    if [[ "$restart" == no && -z "$existing" ]]; then
        build_images rabbitmq
        load_images rabbitmq
    fi
    kube apply -k "$KUBE_DIR/base/rabbitmq"
    if [[ "$restart" == yes && -n "$existing" ]]; then
        kube rollout restart statefulset/rabbitmq
    fi
    kube rollout status statefulset/rabbitmq --timeout=300s
}

start_mailpit() {
    local restart=$1 existing
    existing=$(kube get deployment mailpit --ignore-not-found -o name)
    kube apply -k "$KUBE_DIR/base/mailpit"
    if [[ "$restart" == yes && -n "$existing" ]]; then
        kube rollout restart deployment/mailpit
    fi
    kube rollout status deployment/mailpit --timeout=180s
}

start_redis() {
    local status
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret redis-credentials "$KUBE_DIR/local/redis/.env"
    kube apply -k "$KUBE_DIR/base/redis"
    if status=$(kube rollout status deployment/redis --timeout=180s 2>&1); then
        printf '%s\n' "$status"
    else
        printf '%s\n' "$status" >&2
        case "${status##*$'\n'}" in
            *'timed out waiting for the condition'*|'error: context deadline exceeded'|*'deployment "redis" exceeded its progress deadline'*)
                printf 'Warning: Redis did not become Ready in time. Continuing deployment. Queries can run without the cache.\n' >&2 ;;
            *) return 1 ;;
        esac
    fi
}

ensure_photo_access() {
    if [[ -z "$(kube get secret listings-photo-access --ignore-not-found -o name)" ]]; then
        need openssl
        openssl rand -hex 32 | tr -d '\n' | kube create secret generic listings-photo-access --from-file=api-key=/dev/stdin
    fi
}

start_matching_database() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_photo_access
    ensure_secret matching-credentials "$KUBE_DIR/local/matching/.env"
    kube apply -f "$KUBE_DIR/base/matching/db-service.yaml" -f "$KUBE_DIR/base/matching/db-statefulset.yaml"
    kube rollout status statefulset/matching-db --timeout=180s
}

start_listings_infrastructure() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_photo_access
    ensure_secret auth-credentials "$KUBE_DIR/local/auth/.env"
    ensure_secret listings-credentials "$KUBE_DIR/local/.env"
    kube apply -f "$KUBE_DIR/base/listings/db-service.yaml" -f "$KUBE_DIR/base/listings/db-statefulset.yaml"
    kube rollout status statefulset/listings-db --timeout=180s
}

start_auth_infrastructure() {
    kube apply -f "$KUBE_DIR/base/namespace.yaml"
    ensure_secret auth-credentials "$KUBE_DIR/local/auth/.env"
    if [[ -z "$(kube get secret auth-email-protection --ignore-not-found -o name)" ]]; then
        need openssl
        local email_key
        email_key=$(openssl rand -base64 32)
        printf '%s' "$email_key" | kube create secret generic auth-email-protection --from-file=encryption-key=/dev/stdin
    fi
    kube apply -f "$KUBE_DIR/base/auth/db-service.yaml" -f "$KUBE_DIR/base/auth/db-statefulset.yaml"
    kube rollout status statefulset/auth-db --timeout=180s
    start_mailpit "$1"
}

run_migration_job() {
    local job_name=$1 manifest=$2 log_target=$3
    local previous name complete failed
    previous=$(kube get job "$job_name" --ignore-not-found -o 'jsonpath={.metadata.name}|{.status.conditions[?(@.type=="Complete")].status}|{.status.conditions[?(@.type=="Failed")].status}')
    IFS='|' read -r name complete failed <<< "$previous"
    if [[ -n "$name" ]]; then
        if [[ "$complete" != True && "$failed" != True ]]; then
            fail "$job_name is still running. Use ./k8s.sh logs $log_target; it will not be replaced."
        fi
        kube delete job "$job_name" --wait=true
    fi
    kube apply -f "$manifest"
    if ! kube wait --for=condition=complete "job/$job_name" --timeout=300s; then
        kube logs "job/$job_name" --tail=100 || true
        fail "Migration $job_name did not complete. Application rollout was not started; inspect the retained Job."
    fi
    kube logs "job/$job_name" --tail=100
}

run_migration() {
    case "$1" in
        listings) run_migration_job listings-migrate "$KUBE_DIR/migrations/listings-job.yaml" listings-migrations ;;
        matching) run_migration_job matching-migrate "$KUBE_DIR/migrations/matching-job.yaml" matching-migrations ;;
        auth) run_migration_job auth-migrate "$KUBE_DIR/migrations/auth-job.yaml" auth-migrations ;;
        *) fail "Unknown migration target '$1'. Use listings, matching, auth or all." ;;
    esac
}

migrate_target() {
    case "$1" in
        listings|auth|matching)
            build_images "$1-migrations"
            load_images "$1-migrations"
            kube rollout status "statefulset/$1-db" --timeout=180s
            run_migration "$1" ;;
        all)
            migrate_target listings
            migrate_target auth
            migrate_target matching ;;
        *) fail "Unknown migrate target '$1'. Use listings, matching, auth or all." ;;
    esac
}

restart_target() {
    case "$1" in
        api|listings-api) kube rollout restart deployment/listings-service; kube rollout status deployment/listings-service --timeout=180s ;;
        worker|listings-worker) kube rollout restart deployment/listings-outbox-worker; kube rollout status deployment/listings-outbox-worker --timeout=180s ;;
        auth-api) kube rollout restart deployment/auth-service; kube rollout status deployment/auth-service --timeout=180s ;;
        auth-identity) kube rollout restart deployment/auth-identity-worker; kube rollout status deployment/auth-identity-worker --timeout=180s ;;
        auth-worker) kube rollout restart deployment/auth-outbox-worker; kube rollout status deployment/auth-outbox-worker --timeout=180s ;;
        web) kube rollout restart deployment/smartlost-web; kube rollout status deployment/smartlost-web --timeout=180s ;;
        rabbitmq) kube rollout restart statefulset/rabbitmq; kube rollout status statefulset/rabbitmq --timeout=300s ;;
        matching|matching-api) kube rollout restart deployment/matching-service; kube rollout status deployment/matching-service --timeout=180s ;;
        redis) kube rollout restart deployment/redis; kube rollout status deployment/redis --timeout=180s ;;
        mailpit) kube rollout restart deployment/mailpit; kube rollout status deployment/mailpit --timeout=180s ;;
        listings) restart_target listings-api; restart_target listings-worker ;;
        auth) restart_target auth-api; restart_target auth-worker; restart_target auth-identity ;;
        all) restart_target rabbitmq; restart_target mailpit; restart_target listings; restart_target auth; restart_target matching; restart_target web ;;
        *) fail "Unknown restart target '$1'. Use listings-api (api), listings-worker (worker), auth-api, auth-worker, auth-identity, matching, redis, web, listings, auth, rabbitmq, mailpit or all." ;;
    esac
}

deploy_target() {
    local target=$1
    case "$target" in web|listings|auth|matching|rabbitmq|mailpit|all) ;; *) fail "Deploy target must be web, listings, auth, matching, rabbitmq, mailpit or all." ;; esac
    check_cluster
    if [[ "$target" == mailpit ]]; then
        kube apply -f "$KUBE_DIR/base/namespace.yaml"
        start_mailpit yes
        return
    fi
    build_images "$target"
    load_images "$target"
    case "$target" in
        web)
            kube apply -f "$KUBE_DIR/base/namespace.yaml"
            kube apply -k "$KUBE_DIR/base/web"
            restart_target web
            return ;;
        rabbitmq) start_broker yes; return ;;
        all) start_broker yes ;;
        listings|auth|matching) start_broker no ;;
    esac
    case "$target" in listings|matching|all) start_redis ;; esac
    if [[ "$target" == matching ]]; then
        start_matching_database
        run_migration matching
        kube apply -k "$KUBE_DIR/overlays/local" --selector smartlost/service=matching
        restart_target matching
        printf 'Matching Swagger: ./k8s.sh ui matching, then http://localhost:%s/swagger\n' "$MATCHING_LOCAL_PORT"
        return
    fi
    if [[ "$target" == listings ]]; then
        start_listings_infrastructure
        run_migration listings
        kube apply -k "$KUBE_DIR/base/listings"
        kube patch service listings-service --type=merge --patch-file "$KUBE_DIR/overlays/local/listings-nodeport.yaml"
        kube scale deployment/listings-service --replicas=3
        # Local tags and subPath ConfigMap mounts require replacing existing pods.
        restart_target listings
        printf 'Listings Swagger: http://localhost:5082/swagger\nRabbitMQ UI: ./k8s.sh ui rabbitmq\n'
        return
    fi
    if [[ "$target" == auth ]]; then
        start_auth_infrastructure no
        run_migration auth
        kube apply -k "$KUBE_DIR/base/auth"
        kube patch service auth-service --type=merge --patch-file "$KUBE_DIR/overlays/local/auth-nodeport.yaml"
        kube scale deployment/auth-service --replicas=3
        # Local tags and subPath ConfigMap mounts require replacing existing pods.
        restart_target auth
        printf 'Auth Swagger: http://localhost:5080/swagger\nMailpit UI: ./k8s.sh ui mailpit\n'
        return
    fi
    start_listings_infrastructure
    start_auth_infrastructure yes
    start_matching_database
    run_migration listings
    run_migration auth
    run_migration matching
    kube apply -k "$KUBE_DIR/overlays/local"
    # Local tags and subPath ConfigMap mounts require replacing existing pods.
    restart_target listings
    restart_target auth
    restart_target matching
    restart_target web
    printf 'SmartLost web: ./k8s.sh ui web\n'
    printf 'Auth Swagger: http://localhost:5080/swagger\nListings Swagger: http://localhost:5082/swagger\nMatching Swagger: ./k8s.sh ui matching\nMailpit UI: ./k8s.sh ui mailpit\nRabbitMQ UI: ./k8s.sh ui rabbitmq\n'
}

scale_if_exists() {
    local resource=$1 replicas=$2
    if [[ -n "$(kube get "$resource" --ignore-not-found -o name)" ]]; then
        kube scale "$resource" --replicas="$replicas"
    fi
}

logs_target() {
    local resource
    case "$1" in
        api|listings-api) resource=deployment/listings-service ;;
        worker|listings-worker) resource=deployment/listings-outbox-worker ;;
        db|listings-db) resource=statefulset/listings-db ;;
        migrations|listings-migrations) resource=job/listings-migrate ;;
        redis) resource=deployment/redis ;;
        web) resource=deployment/smartlost-web ;;
        matching-api) resource=deployment/matching-service ;;
        matching-db) resource=statefulset/matching-db ;;
        matching-migrations) resource=job/matching-migrate ;;
        auth|auth-api) resource=deployment/auth-service ;;
        auth-identity) resource=deployment/auth-identity-worker ;;
        auth-worker) resource=deployment/auth-outbox-worker ;;
        auth-db) resource=statefulset/auth-db ;;
        auth-migrations) resource=job/auth-migrate ;;
        mailpit) resource=deployment/mailpit ;;
        rabbitmq) resource=statefulset/rabbitmq ;;
        *) fail "Unknown log target '$1'. Use listings-api (api), listings-worker (worker), listings-db (db), listings-migrations (migrations), auth-api (auth), auth-worker, auth-db, auth-migrations, matching-api, matching-db, matching-migrations, redis, web, mailpit or rabbitmq." ;;
    esac
    case "$2" in
        '') kube logs "$resource" --tail=100 ;;
        --follow) kube logs "$resource" --tail=100 --follow ;;
        *) fail "Logs only accepts the optional --follow flag." ;;
    esac
}

command_name=${1:-help}
[[ $# -eq 0 ]] || shift
case "$command_name" in
    logs|migration-add)
        [[ $# -le 2 ]] || fail "Usage: ./k8s.sh $command_name ..." ;;
    *)
        [[ $# -le 1 ]] || fail 'Too many arguments. Run ./k8s.sh help.' ;;
esac
case "$command_name" in
    help|-h|--help) help_text ;;
    config) [[ $# -eq 0 ]] || fail 'config takes no arguments.'; config_files ;;
    up) [[ $# -eq 0 ]] || fail 'up takes no arguments.'; create_cluster; deploy_target all ;;
    deploy) deploy_target "${1:-all}" ;;
    build) build_images "${1:-all}" ;;
    load) check_cluster; load_images "${1:-all}" ;;
    migrate) check_cluster; migrate_target "${1:-all}" ;;
    migration-add)
        local_service=listings
        local_migration_name=${1:-}
        if [[ $# -eq 2 ]]; then
            case "$1" in
                listings|auth|matching) local_service=$1; local_migration_name=$2 ;;
                *) local_service=$2; local_migration_name=$1 ;; # Legacy NAME SERVICE order
            esac
        fi
        [[ -n "$local_migration_name" && "$local_migration_name" =~ ^[A-Za-z][A-Za-z0-9_]*$ ]] || fail 'Provide a valid migration name, e.g. ./k8s.sh migration-add auth AddUserLockout'
        need dotnet; dotnet tool restore
        case "$local_service" in
            listings)
                dotnet ef migrations add "$local_migration_name" --project "$LISTINGS_INFRA_PROJECT" --startup-project "$LISTINGS_INFRA_PROJECT" --context ListingsDbContext --output-dir Persistence/Migrations ;;
            auth)
                dotnet ef migrations add "$local_migration_name" --project "$AUTH_INFRA_PROJECT" --startup-project "$AUTH_INFRA_PROJECT" --context AuthDbContext --output-dir Persistence/Migrations ;;
            matching)
                dotnet ef migrations add "$local_migration_name" --project "$MATCHING_INFRA_PROJECT" --startup-project "$MATCHING_INFRA_PROJECT" --context MatchingDbContext --output-dir Persistence/Migrations ;;
            *) fail "Unknown service '$local_service'. Use listings, auth or matching." ;;
        esac ;;
    restart) check_cluster; restart_target "${1:-all}" ;;
    stop)
        [[ $# -eq 0 ]] || fail 'stop takes no arguments.'
        check_cluster
        scale_if_exists deployment/smartlost-web 0
        scale_if_exists deployment/matching-service 0
        scale_if_exists deployment/auth-service 0
        scale_if_exists deployment/auth-identity-worker 0
        scale_if_exists deployment/auth-outbox-worker 0
        scale_if_exists deployment/listings-service 0
        scale_if_exists deployment/listings-outbox-worker 0
        kube wait --for=delete pod -l app.kubernetes.io/name=auth-service --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=auth-identity-worker --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=auth-outbox-worker --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=listings-service --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=listings-outbox-worker --timeout=180s
        kube wait --for=delete pod -l app.kubernetes.io/name=matching-service --timeout=180s
        scale_if_exists deployment/redis 0
        scale_if_exists statefulset/matching-db 0
        scale_if_exists deployment/mailpit 0
        scale_if_exists statefulset/rabbitmq 0
        scale_if_exists statefulset/auth-db 0
        scale_if_exists statefulset/listings-db 0 ;;
    status)
        [[ $# -eq 0 ]] || fail 'status takes no arguments.'
        check_cluster; kube get deployments,statefulsets,pods,services,jobs,pvc ;;
    logs) check_cluster; logs_target "${1:-worker}" "${2:-}" ;;
    ui)
        check_cluster
        case "${1:-rabbitmq}" in
            web)
                printf 'SmartLost: http://localhost:4200 (Ctrl+C stops forwarding).\n'
                kube port-forward service/smartlost-web 4200:80 ;;
            rabbitmq)
                printf 'RabbitMQ: http://localhost:15672 (credentials in deploy/kubernetes/local/rabbitmq/.env). Ctrl+C stops forwarding.\n'
                kube port-forward service/rabbitmq 15672:15672 ;;
            mailpit)
                printf 'Mailpit: http://localhost:8025 (captures Auth confirmation emails). Ctrl+C stops forwarding.\n'
                kube port-forward service/mailpit 8025:8025 ;;
            matching)
                printf 'Matching Swagger: http://localhost:%s/swagger (Ctrl+C stops forwarding).\n' "$MATCHING_LOCAL_PORT"
                kube port-forward service/matching-service "$MATCHING_LOCAL_PORT:80" ;;
            *) fail "Unknown UI target '$1'. Use web, rabbitmq, mailpit or matching." ;;
        esac ;;
    validate)
        [[ $# -eq 0 ]] || fail 'validate takes no arguments.'
        need kubectl; kubectl kustomize "$KUBE_DIR/overlays/local" >/dev/null
        printf 'Local Kustomize overlay rendered successfully; no cluster changes applied.\n' ;;
    *) fail "Unknown command '$command_name'. Run ./k8s.sh help." ;;
esac
