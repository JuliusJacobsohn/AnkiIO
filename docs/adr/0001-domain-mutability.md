# ADR 0001: Controlled mutation

Domain objects use controlled mutation while being assembled and expose read-only collections. This keeps the ordinary API fluent while preventing callers from bypassing invariants through list replacement.

