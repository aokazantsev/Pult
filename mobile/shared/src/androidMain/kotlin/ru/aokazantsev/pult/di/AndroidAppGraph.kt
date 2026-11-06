package ru.aokazantsev.pult.di

import android.content.Context
import dev.zacsweers.metro.createGraphFactory
import io.ktor.client.HttpClient
import io.ktor.client.engine.okhttp.OkHttp
import java.util.concurrent.TimeUnit
import ru.aokazantsev.pult.relay.SharedPreferencesSettingsStore

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
        settingsStore = SharedPreferencesSettingsStore(context),
    )

private const val CONNECT_TIMEOUT_SECONDS = 10L
