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
POLLING_DELAY=1000                           # Milliseconds to wait between runs
RUN_ONCE=1                                   # Only run once - use this to switch a cron job instead of 24/7 monitor run
JOB_PREFIX=agent-job-                        # Customize the agent job's prefix
JOB_DOCKER_SOCKET_PATH=/var/run/docker.sock  # Set this to allow for docker builds within your docker container
JOB_DEFINITION_FILE=job.yaml                 # Provide a template for the k8s Jobs the orchestrator creates
```

## Customizing the Kubernetes Job
In many scenarios you will want to specify additional configurations to the Job that the orchestrator creates in your k8s cluster. For example, perhaps your pipelines require a custom mounted set of secrets from a CSI, or you would like to reserve memory/cpu for each job, or mount a cached set of build assets. To allow for this level of customization you can now specify the `JOB_DEFINITION_FILE` env variable which will provide you a way of define all the bells and whistles you need for you pipeline agents.

A sample custom job file might look like this:
```
apiVersion: batch/v1
kind: Job
metadata:
  name: custom-job
spec:
  template:
    spec:
      containers:
      - name: custom-job
        image: ghcr.io/akanieski/ado-pipelines-linux:latest
        resources:
          requests:
            memory: "100Mi"
            cpu: "1"
          limits:
            memory: "200Mi"
            cpu: "2"
        env:
          - name: AZP_URL
            value: https://dev.azure.com/your-org
          - name: AZP_TOKEN
            value: xxxqhugutbqvpoxxxicdab2ojaipkw6kexxxau57bybmvksp5jpq
          - name: AZP_POOL
            value: Default
      restartPolicy: Never
```
