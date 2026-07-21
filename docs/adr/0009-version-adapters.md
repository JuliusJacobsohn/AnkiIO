# ADR 0009: Version adapters

Detection and capability selection use `IAnkiVersionAdapter`. Unknown versions fail with an actionable message; adjacent version numbers are never assumed compatible.

