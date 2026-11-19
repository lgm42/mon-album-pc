package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.data.media.BackupRunEntity
import io.github.lgm42.monalbumpc.data.media.RunEnd
import io.github.lgm42.monalbumpc.data.media.end
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class RunHistoryTest {

    private fun run(
        end: RunEnd,
        started: Long,
        sent: Int = 0,
        recognized: Int = 0,
        failed: Int = 0,
        id: Long = 0,
        detail: String? = null,
    ) = BackupRunEntity(
        id = id,
        startedAt = started,
        endedAt = started + 10,
        queued = 5,
        sent = sent,
        recognized = recognized,
        failed = failed,
        sentBytes = sent * 100L,
        outcome = end.name,
        attempts = 1,
        detail = detail,
    )

    @Test
    fun `runs that end the same way without moving a file extend the previous one`() {
        val previous = run(RunEnd.UNREACHABLE, started = 1_000, id = 7, detail = "nas.local:48620")
        val next = run(RunEnd.UNREACHABLE, started = 2_000)

        val merged = RunHistory.merge(previous, next)

        assertEquals(7L, merged.id)
        assertEquals(1_000L, merged.startedAt)
        assertEquals(2_010L, merged.endedAt)
        assertEquals(2, merged.attempts)
        assertEquals("nas.local:48620", merged.detail)
    }

    @Test
    fun `a run that did something, or ended otherwise, gets its own row`() {
        val unreachable = run(RunEnd.UNREACHABLE, started = 1_000, id = 7)
        val completed = run(RunEnd.COMPLETED, started = 2_000, sent = 3)
        assertSame(completed, RunHistory.merge(unreachable, completed))

        val interrupted = run(RunEnd.INTERRUPTED, started = 3_000, sent = 1)
        assertSame(interrupted, RunHistory.merge(run(RunEnd.INTERRUPTED, started = 2_000, id = 8), interrupted))

        val first = run(RunEnd.BUSY, started = 1_000)
        assertSame(first, RunHistory.merge(null, first))
        assertSame(first, RunHistory.merge(run(RunEnd.BUSY, started = 500, failed = 1, id = 3), first))
    }

    @Test
    fun `unknown outcomes read as interrupted`() {
        assertEquals(RunEnd.INTERRUPTED, run(RunEnd.COMPLETED, 0).copy(outcome = "SOMETHING_NEW").end)
        assertEquals(RunEnd.PAUSED, run(RunEnd.PAUSED, 0).end)
    }
}
