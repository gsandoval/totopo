#!/bin/sh
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


HTTP_PORT=${PORT:-50001}
RESOURCES_BUCKET_BASE_URI_VAR=${RESOURCES_BUCKET_BASE_URI:-totopo.ker.gs}
RESOURCES_CDN_BASE_URL_VAR=${RESOURCES_CDN_BASE_URL:-https://storage.googleapis.com/hac.ker.gs}

/app/Totopo \
    --http-port $HTTP_PORT \
    --totopo-resources-path /app/resources/totopo \
    --application-resources-path /app/resources/hackergs \
    --resources-bucket-base-uri $RESOURCES_BUCKET_BASE_URI_VAR \
    --resources-cdn-base-url $RESOURCES_CDN_BASE_URL_VAR