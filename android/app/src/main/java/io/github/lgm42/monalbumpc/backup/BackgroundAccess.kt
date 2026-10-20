package io.github.lgm42.monalbumpc.backup

import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings

/**
 * Battery restrictions decide whether backups can run on their own: a restricted app is not allowed to start its
 * transfer in the foreground from the background, so Android stops it after a few minutes and delays retries.
 */
object BackgroundAccess {

    fun isUnrestricted(context: Context): Boolean {
        val power = context.getSystemService(Context.POWER_SERVICE) as PowerManager
        return power.isIgnoringBatteryOptimizations(context.packageName)
    }

    /** System dialog "Autoriser l'app à toujours s'exécuter en arrière-plan ?". */
    @SuppressLint("BatteryLife") // the whole point of the app is to run in the background; not on the Play Store
    fun requestUnrestricted(context: Context): Intent =
        Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:${context.packageName}"))

    /** App details screen, where Xiaomi phones put "Démarrage automatique" and the battery saver. */
    fun appDetails(context: Context): Intent =
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}"))

    /** Xiaomi, Redmi and POCO (MIUI / HyperOS) also stop apps that lack "Démarrage automatique". */
    val isXiaomi: Boolean
        get() = Build.MANUFACTURER.lowercase() in setOf("xiaomi", "redmi", "poco")
}
