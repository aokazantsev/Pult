package ru.aokazantsev.pult.di

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.DependencyGraph
import dev.zacsweers.metro.Provides
import io.ktor.client.HttpClient
import ru.aokazantsev.pult.access.AppAccess
import ru.aokazantsev.pult.access.DeviceAuthenticator
import ru.aokazantsev.pult.access.ElapsedClock
import ru.aokazantsev.pult.crypto.DeviceKeyStore
import ru.aokazantsev.pult.crypto.PultCrypto
import ru.aokazantsev.pult.pairing.PairStore
import ru.aokazantsev.pult.pairing.PairingController
import ru.aokazantsev.pult.ui.main.MainViewModel
import ru.aokazantsev.pult.ui.pair.PairViewModel
import ru.aokazantsev.pult.ui.settings.SettingsViewModel
import ru.aokazantsev.pult.ui.thread.ThreadViewModel

@DependencyGraph(AppScope::class)
interface AppGraph {
    val appInfo: AppInfo
    val pairingController: PairingController
    val pairStore: PairStore
    val deviceKeyStore: DeviceKeyStore
    val appAccess: AppAccess
    val mainViewModel: () -> MainViewModel
    val pairViewModel: () -> PairViewModel
    val settingsViewModel: () -> SettingsViewModel
    val threadViewModel: () -> ThreadViewModel

    @DependencyGraph.Factory
    fun interface Factory {
        fun create(
            @Provides appInfo: AppInfo,
            @Provides httpClient: HttpClient,
            @Provides pairStore: PairStore,
            @Provides deviceKeyStore: DeviceKeyStore,
            @Provides crypto: PultCrypto,
            @Provides clock: ElapsedClock,
            @Provides authenticator: DeviceAuthenticator,
        ): AppGraph
    }
}
