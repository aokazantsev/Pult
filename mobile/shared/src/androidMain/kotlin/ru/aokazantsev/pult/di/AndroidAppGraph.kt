package ru.aokazantsev.pult.di

import android.content.Context
import dev.zacsweers.metro.createGraphFactory
import io.ktor.client.HttpClient
import io.ktor.client.engine.okhttp.OkHttp
import java.util.concurrent.TimeUnit
import ru.aokazantsev.pult.access.AndroidDeviceAuthenticator
import ru.aokazantsev.pult.access.AndroidElapsedClock
import ru.aokazantsev.pult.access.DebugLockOptionalAuthenticator
import ru.aokazantsev.pult.crypto.AndroidDeviceKeyStore
import ru.aokazantsev.pult.crypto.JcaCrypto
import ru.aokazantsev.pult.pairing.SharedPreferencesPairStore

fun createAndroidAppGraph(context: Context, appInfo: AppInfo): AppGraph =
    createGraphFactory<AppGraph.Factory>().create(
        appInfo = appInfo,
        httpClient = HttpClient(OkHttp) {
            engine {
                config {
                    readTimeout(0, TimeUnit.SECONDS)
                    connectTimeout(CONNECT_TIMEOUT_SECONDS, TimeUnit.SECONDS)
                }
            }
        },
        pairStore = SharedPreferencesPairStore(context),
        deviceKeyStore = AndroidDeviceKeyStore(),
        crypto = JcaCrypto(),
        clock = AndroidElapsedClock(),
        authenticator = AndroidDeviceAuthenticator(context).let { device ->
            if (appInfo.isDebug) DebugLockOptionalAuthenticator(device) else device
        },
    )

private const val CONNECT_TIMEOUT_SECONDS = 10L
