package io.github.lgm42.monalbumpc.data

import android.annotation.SuppressLint
import android.content.Context
import android.os.Build
import android.provider.Settings
import io.github.lgm42.monalbumpc.BuildConfig
import io.github.lgm42.monalbumpc.net.DeviceInfo

/** Describes this phone to the PC when pairing (protocol §3.3). */
object DeviceInfoFactory {
    // The Android ID is scoped to this app and its signing key: it identifies the phone to its own PC only.
    @SuppressLint("HardwareIds")
    fun create(context: Context): DeviceInfo {
        val model = "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}"
        val name = Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)
            ?.takeIf { it.isNotBlank() }
            ?: model
        return DeviceInfo(
            name = name,
            model = model,
            androidVersion = Build.VERSION.RELEASE,
            appVersion = BuildConfig.VERSION_NAME,
            appVersionCode = BuildConfig.VERSION_CODE,
            hardwareId = Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID),
        )
    }
}
