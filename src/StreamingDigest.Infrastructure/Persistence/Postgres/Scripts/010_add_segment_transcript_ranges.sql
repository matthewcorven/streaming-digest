CREATE TABLE IF NOT EXISTS public.segment_transcript_ranges (
    segment_id uuid NOT NULL REFERENCES public.segments(id) ON DELETE CASCADE,
    transcript_cue_id uuid NOT NULL REFERENCES public.transcript_cues(id) ON DELETE CASCADE,
    PRIMARY KEY (segment_id, transcript_cue_id)
);

CREATE INDEX IF NOT EXISTS idx_segment_transcript_ranges_segment_id
    ON public.segment_transcript_ranges (segment_id);

CREATE INDEX IF NOT EXISTS idx_segment_transcript_ranges_cue_id
    ON public.segment_transcript_ranges (transcript_cue_id);
