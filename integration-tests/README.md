# Integration Tests

The integration tests are designed to test the `azure-pipelines-orchestrator` on an actual Kubernetes cluster against a live Azure DevOps repo. During the test, a [kind cluster](https://kind.sigs.k8s.io/) is provisioned using the `kind-config.yaml` file that has both a local docker registry, and an `nginx` Ingress. The orchestrator is deployed, and a temporary pipeline is created for the life of tht test. Then, a pipeline job is queued to run and the script will monitor to see if the orchestrator correctly spawns a corresponding `Job` object in K8s.

## Test Parameters

Within the `.github/workflows/integration-tests.yaml` file the following environment variables are set:

- `ADO_PROJECT`   - The Azure DevOps project where the test pipeline will run
- `PIPELINE_NAME` - The name of the pipeline to create (a random number will be appended to this name)
- `PIPELINE_REPO` - The name of the repo in the ADO project that contains a test `azure-pipelines.yml` file
- `JOB_IMAGE`     - The agent image to run
- `AGENT_POOLS`   - The agent pool(s) to use
- `NAMESPACE`     - The k8s namespace to create all the resources in
- `TEST_TIMEOUT`  - The amount of time to wait before assuming the test has failed

## Secrets

This integration test scripts require several secrets to exist in an environment called `integration`:

- `ORG_URL`
    - The ADO organization URL to use for the test
- `ORG_PAT`
    - The Personal Access Token for the ADO Organization. Requires the same permissions as for when provisoning an agent. Additionally, this pat should have permission to manage Service Connections and read Source Code.
