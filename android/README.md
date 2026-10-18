# Mon Album PC — app Android

Kotlin + Jetpack Compose (Material 3). Identifiant : `io.github.lgm42.monalbumpc`, Android 10 minimum.

## Compilation

L'APK est compilé par GitHub Actions (`.github/workflows/build.yml`) : à chaque push, l'APK de debug
est disponible dans les artefacts du job `android` (`monalbumpc-debug-apk`).

Le repo ne contient **pas de wrapper Gradle** (`gradlew`) : la CI installe Gradle 8.11.1 avec
`gradle/actions/setup-gradle` et lance `gradle` directement.

Pour compiler sur un PC (facultatif), il faut JDK 17, Gradle 8.11.1 et le SDK Android (variable
`ANDROID_HOME` ou fichier `local.properties` avec `sdk.dir=...`), puis, depuis ce dossier :

```
gradle :app:testDebugUnitTest :app:assembleDebug
```

L'APK se trouve ensuite dans `app/build/outputs/apk/debug/`. Android Studio ouvre aussi le dossier
directement.

## Police

Nunito (police variable, `app/src/main/res/font/nunito_variable.ttf`), sous licence SIL Open Font
License 1.1 : voir `third_party/nunito-OFL.txt`.
