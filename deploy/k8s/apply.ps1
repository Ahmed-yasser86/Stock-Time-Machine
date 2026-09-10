# Applies the Stock Time Machine manifests to a Kubernetes cluster.
# Run from this directory:  .\apply.ps1
# Requires: kubectl connected to the target cluster.
# Before first apply, edit for your environment:
#   - "Config and Secrets Folder/Config.yaml": Qdrant__Host (Qdrant Cloud host)
#   - "Config and Secrets Folder/stocksapp-secret.yaml": real credentials
#   - Deployments/StocksApp-deployment.yaml: image names (registry + tag)
#   - Deployments/StocksAppFrontend-deployment.yaml: rebuild frontend image
#     with --build-arg VITE_API_URL pointing at stocksapp-service
# WARNING: "Config and Secrets Folder/" contains tracked placeholder secrets.
# Rotate real credentials out-of-band before applying outside local dev.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

kubectl apply -f Stocks_namespace.yaml
kubectl apply -f "Config and Secrets Folder/" -n stocksapp
kubectl apply -f Services/ -n stocksapp
kubectl apply -f Deployments/ -n stocksapp
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml
kubectl apply -f hpa.yaml -n stocksapp

kubectl get all -n stocksapp
kubectl get hpa -n stocksapp
