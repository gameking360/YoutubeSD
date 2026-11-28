FROM eclipse-temurin:25-jdk AS build
ARG OTEL_AGENT_VERSION=2.33.1
WORKDIR /app
COPY .mvn .mvn
COPY mvnw pom.xml ./
RUN --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then keytool -importcert -noprompt -alias build-ca -cacerts -storepass changeit -file /run/secrets/build_ca; fi \
    && chmod +x mvnw && ./mvnw -B -q dependency:go-offline \
    && ./mvnw -B -q dependency:copy -Dartifact=io.opentelemetry.javaagent:opentelemetry-javaagent:${OTEL_AGENT_VERSION} \
       -DoutputDirectory=/agent -Dmdep.stripVersion=true
COPY src src
RUN ./mvnw -B -q package -DskipTests

FROM eclipse-temurin:25-jre
WORKDIR /app
RUN useradd --system app
USER app
COPY --from=build /agent/opentelemetry-javaagent.jar opentelemetry-javaagent.jar
COPY --from=build /app/target/download-*.jar app.jar
ENV OTEL_SERVICE_NAME=download
EXPOSE 8080
ENTRYPOINT ["java", "-javaagent:opentelemetry-javaagent.jar", "-XX:MaxRAMPercentage=75", "-jar", "app.jar"]
