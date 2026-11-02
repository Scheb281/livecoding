FROM alpine:3.23
RUN apk add --no-cache openssl
COPY generate.sh /usr/local/bin/generate
RUN chmod 0555 /usr/local/bin/generate
ENTRYPOINT ["generate"]
