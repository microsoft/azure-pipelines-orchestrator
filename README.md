- Get List of Agents including assiments https://dev.azure.com/devops-collab/_apis/distributedtask/pools/1/agents?includeAssignedRequest=true

- Iterate over Agents and set "IsBusy" depending on if assignedRequest != null

- Get List of JobRequests

- Iterate JobRequests Where result is blank/null (not failed and not success)

- If JobRequest has a "reservedAgent" that is categorized as "IsBusy" AND the a NewAgentRequest is not already present for the given RequestId .. otherwise then request additional agent [Event: NewAgentRequest]

- Track the [Event: NewAgentRequest] in memory

- After completed iterations then backoff for X seconds to allow for new agent to provision# ado-agent-orchestrator
