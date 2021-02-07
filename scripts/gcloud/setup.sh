#!/bin/bash

# Copyright 2020 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

PROJECT_ID=ker-gs
SERVICE_ACCOUNT_ID=hackergs
SERVICE_ACCOUNT_DISPLAY=${SERVICE_ACCOUNT_ID}
GITHUB_OWNER=gsandoval
GITHUB_REPO=totopo
GITHUB_TAG_TRIGGER="release-.*"

RUN_MIN_INSTANCES=1
RUN_MAX_INSTANCES=20
RUN_INSTANCE_NAME=kergs
RUN_REGION=us-east1

TOTOPO_BUCKET_URI=totopo.ker.gs
TOTOPO_CDN_URL=https://storage.googleapis.com/hac.ker.gs

die() 
{
  if [ "${1}" -ne "0" ]; then
    printf "${2}\n"
    exit ${1}
  fi
}

SERVICE_ACCOUNT_NAME=${SERVICE_ACCOUNT_ID}@${PROJECT_ID}.iam.gserviceaccount.com
printf "Fetching project number for ${PROJECT_ID}... "
PROJECT_NUMBER=`gcloud projects describe ${PROJECT_ID} --format="value(projectNumber)"`
die $? "Failed to get projectNumber."
printf "Success.\n"

gcloud iam service-accounts describe ${SERVICE_ACCOUNT_NAME} &> /dev/null
service_account_found=$?
if [ $service_account_found -ne 0 ]; then
  printf "Creating service account ${SERVICE_ACCOUNT_ID} in project ${PROJECT_ID}... "
  gcloud iam service-accounts create ${SERVICE_ACCOUNT_ID} \
    --description="Account used to build and run totopo instances" \
    --display-name="${SERVICE_ACCOUNT_DISPLAY}"
  die $? "Failed to create service account ${SERVICE_ACCOUNT_ID}."
  printf "Success.\n"

  printf "Granting permissions to service account to Cloud Build... "
  gcloud iam service-accounts add-iam-policy-binding \
    ${SERVICE_ACCOUNT_NAME} \
    --member="serviceAccount:${PROJECT_NUMBER}@cloudbuild.gserviceaccount.com" \
    --role="roles/iam.serviceAccountUser"
  die $? "Failed to grant permissions to Cloud Build to ${SERVICE_ACCOUNT_ID}."
  printf "Success.\n"
else
  printf "Skipping service account creation: ${SERVICE_ACCOUNT_ID} exists in project ${PROJECT_ID}.\n"
fi

tag_trigger_name=totopo-${RUN_INSTANCE_NAME}-build

gcloud beta builds triggers describe ${tag_trigger_name} &> /dev/null
tag_trigger_found=$?
BUILD_SUBSTITUTIONS=_RUN_INSTANCE_NAME="${RUN_INSTANCE_NAME}",_RUN_SERVICE_ACCOUNT="${SERVICE_ACCOUNT_NAME}",_RUN_MIN_INSTANCES="${RUN_MIN_INSTANCES}",_RUN_MAX_INSTANCES="${RUN_MAX_INSTANCES}",_RUN_REGION="${RUN_REGION}",_TOTOPO_LOG_LEVEL="${TOTOPO_LOG_LEVEL}",_TOTOPO_BUCKET_URI="${TOTOPO_BUCKET_URI}",_TOTOPO_CDN_URL="${TOTOPO_CDN_URL}",_TOTOPO_TEMPLATE_CACHE_TTL="${TOTOPO_TEMPLATE_CACHE_TTL}"
if [ $tag_trigger_found -ne 0 ]; then
  printf "Creating build trigger ${tag_trigger_name} for ${GITHUB_OWNER}/${GITHUB_REPO}... "
  gcloud beta builds triggers create github \
    --repo-name=${GITHUB_REPO} \
    --repo-owner=${GITHUB_OWNER} \
    --tag-pattern="${GITHUB_TAG_TRIGGER}" \
    --build-config="scripts/gcloud/cloudbuild.yaml" \
    --description="Builds on pushes with tag ${GITHUB_TAG_TRIGGER}" \
    --name=${tag_trigger_name} \
    --substitutions=${BUILD_SUBSTITUTIONS}
  die $? "Failed to create Cloud Build trigger ${tag_trigger_name}."
  printf "Success.\n"
else
  printf "Skipping trigger creation ${tag_trigger_name}.\n"
fi

#gcloud builds submit \
#  --config scripts/gcloud/cloudbuild.yaml \
#  --substitutions=${BUILD_SUBSTITUTIONS} \
#  ./
