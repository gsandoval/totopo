#!/bin/sh

HTTP_PORT=${PORT:-50001}

/app/Totopo --http-port $HTTP_PORT --totopo-resources-path /app/resources/totopo --application-resources-path /app/resources/hackergs