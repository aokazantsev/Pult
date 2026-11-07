package ru.aokazantsev.pult.ui.thread

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import ru.aokazantsev.pult.designsystem.component.PultCard
import ru.aokazantsev.pult.designsystem.component.PultError
import ru.aokazantsev.pult.designsystem.component.PultLoading
import ru.aokazantsev.pult.designsystem.component.PultScaffold
import ru.aokazantsev.pult.designsystem.component.PultTopBar
import ru.aokazantsev.pult.designsystem.theme.PultTheme
import ru.aokazantsev.pult.render.TextSanitizer
import ru.aokazantsev.pult.ui.Labels
import ru.aokazantsev.pult.ui.render.UntrustedLine

@Composable
fun ThreadScreen(threadId: String, viewModel: ThreadViewModel, onBack: () -> Unit) {
    val state by viewModel.state.collectAsState()
    val content = state.content
    val row = content.row(threadId)
    val timeline = remember(content.slices[threadId], content.cards[threadId]) { content.timeline(threadId) }
    val listState = rememberLazyListState()
    val follow = remember(listState) {
        {
            val last = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
            last >= listState.layoutInfo.totalItemsCount - FOLLOW_TOLERANCE
        }
    }
    val banners = row?.let {
        listOfNotNull(Labels.PC_DIALOG.takeIf { _ -> it.info.pcDialog }, Labels.turnNote(it.info.turnNote))
    }.orEmpty()
    val firstLoad = rememberSaveable { mutableStateOf(true) }
    LaunchedEffect(timeline.size) {
        if (timeline.isNotEmpty() && (firstLoad.value || follow())) {
            listState.scrollToItem(timeline.lastIndex + if (banners.isEmpty()) 0 else 1)
        }
        firstLoad.value = false
    }
    PultScaffold(
        topBar = {
            PultTopBar(
                title = TextSanitizer.oneLine(row?.info?.title ?: "Тред"),
                subtitle = row?.let { TextSanitizer.oneLine(Labels.status(it.info)) },
                onBack = onBack,
            )
        },
    ) { padding ->
        if (row == null && !content.snapshotApplied) {
            PultLoading(modifier = Modifier.padding(padding))
        } else if (row == null) {
            PultError(message = "Треда больше нет в сессии", actionText = "К списку", onAction = onBack, modifier = Modifier.padding(padding))
        } else {
            LazyColumn(
                state = listState,
                modifier = Modifier.padding(padding),
                contentPadding = PaddingValues(PultTheme.spacing.l),
                verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m),
            ) {
                if (banners.isNotEmpty()) {
                    item(key = "banners") {
                        PultCard {
                            banners.forEach { UntrustedLine(text = it, maxLines = 3, color = PultTheme.colors.tertiary) }
                        }
                    }
                }
                items(timeline, key = { it.key }) { item ->
                    TimelineItemView(
                        item = item,
                        draft = content.drafts[threadId],
                        onAllow = viewModel::allow,
                        onDeny = viewModel::deny,
                        onChoose = viewModel::choose,
                        onSendText = viewModel::sendText,
                    )
                }
            }
        }
    }
}

private const val FOLLOW_TOLERANCE = 3
