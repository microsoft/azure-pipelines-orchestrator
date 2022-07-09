# Azure Pipelines - Kubernetes Orchestrator
Many enterprise customers run their own Kubernetes clusters either on-premise or in managed kubernetes environments in the cloud. Azure DevOps Services and Server agents can run from containers hosted in these Kubernetes clusters, but what if you do not want to run your agents 24/7? What if you need to be able to scale the number of agents dynamically as pipelines jobs are queued?

This project provides an application that can monitor a configurable set of agent pools, when pipeline jobs are queued up it will automagically provision Kubernetes Jobs for each job that is queued up. The Kubernetes Jobs will run and process only a single Pipelines Job and then be cleaned up by Kubernetes. 

This allows for horizontally scaleable, on-demand agent pools backed by Kubernetes!

## Getting Started
You can first build the docker image:
```
# Build Orchestrator Container
docker build -t ado-agent-orchestrator

# Build Linux Pipelines Agent
cd linux
docker build -t ado-pipelines-linux
```

## Run with Docker
```
docker run -d --name ado-agent-orchestrator \
    --restart=always \
    --env ORG_URL=https://dev.azure.com/yourorg \
    --env ORG_PAT=12345 \
    --env AGENT_POOLS=Pool1,Pool2 \
    --env JOB_IMAGE=ghcr.io/akanieski/ado-pipelines-linux:latest \
    --env JOB_NAMESPACE=ado \
    ado-agent-orchestrator:latest
```

## Run with Kubernetes
```
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
        image: ghcr.io/akanieski/ado-orchestrator:latest
        env:
        - name: ORG_URL
          value: "https://dev.azure.com/yourorg"
        - name: ORG_PAT
          value: "1234"
        - name: AGENT_POOLS
          value: "Pool1,Pool2"
        - name: JOB_IMAGE
          value: "ghcr.io/akanieski/ado-pipelines-linux:latest"
        - name: JOB_NAMESPACE
          value: "ado"

```
Additionally you can configure the following options environment variables.
```
POLLING_DELAY=1000     # Milliseconds to wait between runs
RUN_ONCE=1             # Only run once - use this to switch a cron job instead of 24/7 monitor run
JOB_PREFIX=agent-job-  # Customize the agent job's prefix
```