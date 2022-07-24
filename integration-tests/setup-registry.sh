#!/bin/bash

NAMESPACE=default

# Create the ingress namespace
kubectl create namespace ingress-nginx

# Deploy Ngnix
kubectl apply -n ingress-nginx -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml

# Wait for it to be ready
kubectl wait --namespace ingress-nginx \
  --for=condition=ready pod \
  --selector=app.kubernetes.io/component=controller \
  --timeout=90s

# Deploy the registry onto kubernetes
kubectl apply -n ${NAMESPACE} -f - << EOF
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: registry-ingress
  annotations:
    nginx.ingress.kubernetes.io/proxy-body-size: "0"
spec:
  rules:
  - http:
      paths:
      - pathType: Prefix
        path: "/"
        backend:
          service:
            name: registry
            port:
              number: 5000
---
kind: Service
apiVersion: v1
metadata:
  name: registry
spec:
  selector:
    app: registry
  ports:
  - port: 5000
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: registry-deployment
  labels:
    app: registry
spec:
  replicas: 1
  selector:
    matchLabels:
      app: registry
  template:
    metadata:
      labels:
        app: registry
    spec:
      containers:
      - name: registry
        image: registry:2
EOF

# Wait for the registry to be ready
kubectl wait deployment/registry-deployment -n ${NAMESPACE} --for condition=Available --timeout=60s

# Wait for the registry to be fully up
for i in {1..10}
do
  echo "Checking registry status..."
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" localhost/v2/)

  if [ "${STATUS}" -le 399 ]; then
    echo "Registry ready with ${STATUS}!"
    break
  fi

  echo "Registry returned ${STATUS}. Trying again in 5 seconds"

  sleep 5s

done