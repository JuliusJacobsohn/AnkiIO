# Advanced scheduling

Review cards normally use type/queue `Review`, a due day, a positive day interval, an ease factor such as 2500, repetition count, and lapse count. Learning/relearning cards use learning queues and packed remaining-step data. Filtered-deck state uses original deck/due fields. Custom scheduler data is opaque.

Anki versions can reinterpret fields. Explicit writes should be limited to an adapter verified against the exact target version.

