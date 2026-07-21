# Validation

Diagnostics include severity, code, message, location, deck/note/card identifiers, field/media context, and remediation. Errors include duplicate IDs, missing type structure, unknown template fields, inconsistent scheduler queue/type, review-derived values on new cards, and invalid flags. Writers throw `AnkiValidationException` carrying the full result.

