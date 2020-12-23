#!/bin/sh

HTTP_PORT=${PORT:-50001}
RESOURCES_BUCKET_BASE_URI_VAR=${RESOURCES_BUCKET_BASE_URI:-totopo.ker.gs}
RESOURCES_CDN_BASE_URL_VAR=${RESOURCES_CDN_BASE_URL:-https://storage.googleapis.com/hac.ker.gs}

/app/Totopo \
    --http-port $HTTP_PORT \
    --totopo-resources-path /app/resources/totopo \
    --application-resources-path /app/resources/hackergs \
    --resources-bucket-base-uri $RESOURCES_BUCKET_BASE_URI_VAR \
    --resources-cdn-base-url $RESOURCES_CDN_BASE_URL_VAR