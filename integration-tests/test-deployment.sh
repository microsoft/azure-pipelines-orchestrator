#!/bin/bash

####
# This script will test actually spinning up the azure-pipelines-orchestrator
# onto a k8s cluster. Afterwards, it will create a pipeline that exists just
# for the test to ensure the orchestrator provisions the agent
#
# Assumes the following environment variables are set:
#
#   ADO_PROJECT   - The Azure DevOps project where the test pipeline will run
#   PIPELINE_NAME - The name of the pipeline to trigger (a random number will be appended to this name)
#   PIPELINE_REPO - The name of the repo in the ADO project that contains a test azure-pipelines.yml
#   JOB_IMAGE     - The agent image to run
#   AGENT_POOLS   - The agent pool(s) to use
#   NAMESPACE     - The k8s namespace to create all the resources in
#   TEST_TIMEOUT  - The amount of time to wait before assuming the test has failed
####

# Exit 1 on ANY error
set -o pipefail

# The image of azure-pipelines-orchestrator to test
IMAGE_TO_TEST=$1
# The Azue DevOps Org URL
ORG_URL=$2
# The Azure DevOps Personal Access Token
ORG_PAT=$3

function log() {
    TIMESTAMP=$(date +"%Y-%m-%dT%H:%M:%S")
    LEVEL=${2:-INFO}
    echo "[${LEVEL}][${TIMESTAMP}] ${1}"
}

function cleanupPipeline() {
  log "Deleting Pipeline ${1}"
  az pipelines delete --id "${1}" --org "${ORG_URL}" -p "${ADO_PROJECT}" --yes
}

log "-- Starting integration test ---"

# Create the namespace if it doesn't exist
kubectl create namespace ${NAMESPACE} 2>/dev/null

log "Granting default service account edit permissions"

# Grant the default service account the ability to
# do most things in the test namespace
kubectl apply -n ${NAMESPACE} -f - << EOF
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: default-edit
subjects:
- kind: ServiceAccount
  name: default # "name" is case sensitive
  namespace: ${NAMESPACE}
roleRef:
  kind: ClusterRole #this must be Role or ClusterRole
  name: edit
  apiGroup: rbac.authorization.k8s.io
EOF

log "Wating ${TEST_TIMEOUT} for orchestrator with image ${IMAGE_TO_TEST}"

# Deploy the agent-orchestrator onto kubernetes
kubectl apply -n ${NAMESPACE} -f - << EOF
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ado-orchestrator-deployment
  labels:
    app: ado-orchestrator
spec:
  replicas: 1
  selector:
    matchLabels:
      app: ado-orchestrator
  template:
    metadata:
      labels:
        app: ado-orchestrator
    spec:
      containers:
      - name: ado-orchestrator
        image: ${IMAGE_TO_TEST}
        env:
        - name: ORG_URL
          value: "${ORG_URL}"
        - name: ORG_PAT
          value: "${ORG_PAT}"
        - name: AGENT_POOLS
          value: "${AGENT_POOLS}"
        - name: JOB_IMAGE
          value: "${JOB_IMAGE}"
        - name: JOB_NAMESPACE
          value: "${NAMESPACE}"
        - name: MINIMUM_AGENT_COUNT
          value: "0"
EOF

# Wait for the deployment to become ready
kubectl wait deployment/ado-orchestrator-deployment -n ${NAMESPACE} --for condition=Available --timeout=${TEST_TIMEOUT}

log "Orchestrator successfully deployed"

log "Logging into Azure"

echo  ${ORG_PAT} | az devops login --organization ${ORG_URL}

log "Asserting there are no jobs"

JOB_COUNT=$(kubectl get job -n ${NAMESPACE} --no-headers | wc -l)

if [ "${JOB_COUNT}" -gt 0 ]; then
    log "Assertion failed: expected 0 jobs, got ${JOB_COUNT}" "ERROR"
    kubectl describe job -n ${NAMESPACE}
    exit 1
fi

RANDOM_PIPELINE_NAME="${PIPELINE_NAME}-${RANDOM}"

log "Creating Pipeline Test pipeline [${RANDOM_PIPELINE_NAME}]"

az pipelines create --name "${RANDOM_PIPELINE_NAME}" \
  --description 'Pipeline for integration tests' \
  --repository "${ORG_URL}/${ADO_PROJECT}/_git/${PIPELINE_REPO}" \
  --project "${ADO_PROJECT}" \
  --branch main \
  --yml-path azure-pipelines.yml

# Fetch the ID of the pipeline so we can remove it later
PIPELINE_ID=$(az pipelines list --org "${ORG_URL}" -p "${ADO_PROJECT}" --name "${RANDOM_PIPELINE_NAME}" -o table | awk '{print $1}' | tail -1)

# try up to 10 times for the job to be created
for i in {1..10}
do
  log "Checking if job exists..."
   JOB_COUNT=$(kubectl get job -n ${NAMESPACE} --no-headers | wc -l)
   # Break if we find the job
   if [ "${JOB_COUNT}" -eq 1 ]; then
    break
   fi
   log "Job Count: ${JOB_COUNT}"
   sleep 5s
done

# Check one more time in case above loop ran 10 times without starting job
JOB_COUNT=$(kubectl get job -n ${NAMESPACE} --no-headers | wc -l)

if [ "${JOB_COUNT}" -ne 1 ]; then
    log "Assertion failed: expected 1 jobs, got ${JOB_COUNT}" "ERROR"
    kubectl describe deployment/ado-orchestrator-deployment -n ${NAMESPACE}
    kubectl logs deployment/ado-orchestrator-deployment -n ${NAMESPACE}
    cleanupPipeline "${PIPELINE_ID}"
    exit 1
fi

JOB_NAME=$(kubectl get job -n ${NAMESPACE} -o=jsonpath="{.items[0].metadata.labels.job-name}")

log "Waiting ${TEST_TIMEOUT} for Job/${JOB_NAME} to finish"

# Wait for job to finish
kubectl wait job/${JOB_NAME} -n ${NAMESPACE} --for condition=Complete --timeout=${TEST_TIMEOUT}

# Output some information about the job, including the logs
kubectl describe job/${JOB_NAME} -n ${NAMESPACE}
kubectl logs job/${JOB_NAME}  -n ${NAMESPACE}

cleanupPipeline "${PIPELINE_ID}"

log "-- Result: SUCCESS ---" 