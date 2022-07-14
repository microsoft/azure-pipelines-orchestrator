# Azure Pipelines - Kubernetes Orchestrator
[![Continuous Integration](https://github.com/akanieski/ado-agent-orchestrator/actions/workflows/ci.yaml/badge.svg?branch=master)](https://github.com/akanieski/ado-agent-orchestrator/actions/workflows/ci.yaml)

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

## Running Serverless with Azure Container Instances
You can also choose to avoid the work of setting up Kubernetes and simply run on Azure Container Instances, as shown below:
```
AZ_SUBSCRIPTION_ID=     # Your Azure Subscription ID
AZ_RESOURCE_GROUP=      # The existing resource group that you will place provisioned container group/instances
AZ_TENANT_ID=           # Your Azure Tenant ID
AZ_REGION=EastUS        # The Azure region your resources are located
AZ_ENVIRONMENT=         # The Azure environment, specify AzurePublicCloud, or other sovereign clouds like AzureChina
```
This feature uses the **"DefaultAzureCredentials"** API for Azure SDK. This allows for a variety of supported Azure credential scenarios. See [these docs](https://docs.microsoft.com/en-us/dotnet/api/azure.identity.environmentcredential?view=azure-dotnet) for more information on how to configure a scenario that works for you. 

## Improving build times through Persistent Volumes
Kubernetes provides users with a convenient mechanism for sharing a disk between multiple containers, or in our case multiple agents and between multiple pipeline runs. We can use this to our advantage. By mounting persistent volumes at key locations you can carry cached data to all of the agents in your pool. 

For example, mounting a persistent volume to the `/root/.nuget/package` path as shown below will make sure you don't have to re-download nuget packages on every single pipeline run.

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
        image: ghcr.io/akanieski/ado-pipelines-linux:0.0.1-preview
        volumeMounts:
          - mountPath: "/root/.nuget/packages"
            name: nuget-cache
      volumes:
        - name: nuget-cache
          persistentVolumeClaim:
            claimName: nuget-cache-claim
---
apiVersion: v1
kind: PersistentVolume
metadata:
  name: nuget-cache
spec:
  capacity:
   storage: 10Gi
  accessModes:
   - ReadWriteMany
  hostPath:
    path: "/tmp/nuget-cache"
  storageClassName: slow

---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: nuget-cache-claim
spec:
  accessModes:
    - ReadWriteMany
  volumeMode: Filesystem
  resources:
    requests:
      storage: 10Gi
  storageClassName: slow

```

Other examples of commonly cached paths:
- `/azp/_work/_tasks` - ADO Pipeline tasks that are downloaded every single time will be cached here - saves time on every run!
- `/azp/_work/_tool` - ADO Tools installer tasks like .NET Tools, NodeJS Tools, etc - saves time on most runs!
- `/root/.npm` - Npm packages are notoriously numerous, mounting a cache here will save lots of time for JS builds
- `/root/.nuget/packages` - Save time on .NET builds

**Note:** the `/root` path above is based on the user/homepath of the user your docker agent runs under. In the examples I use `root` user (not ideal in real world scenarios) for my agent containers. Also note for windows they will also have different paths. The key for both scenarios is that these `/root` paths are the container user's homepath, on Windows it may be `c:\users\agent\` etc.
