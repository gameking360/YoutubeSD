FROM eclipse-temurin:25-jdk AS build
WORKDIR /app
COPY .mvn .mvn
COPY mvnw pom.xml ./
RUN --mount=type=secret,id=build_ca \
    if [ -f /run/secrets/build_ca ]; then keytool -importcert -noprompt -alias build-ca -cacerts -storepass changeit -file /run/secrets/build_ca; fi \
    && chmod +x mvnw && ./mvnw -B -q dependency:go-offline
COPY src src
RUN ./mvnw -B -q package -DskipTests

FROM eclipse-temurin:25-jre
WORKDIR /app
RUN useradd --system app
USER app
COPY --from=build /app/target/download-*.jar app.jar
EXPOSE 8080
ENTRYPOINT ["java", "-XX:MaxRAMPercentage=75", "-jar", "app.jar"]
