// The public surface exposes the underlying PostQuantum.Jwt types (MLDsa,
// PqJwtValidator, etc.) whose shapes are intentionally not CLS-compliant;
// advertise that honestly so CLS-strict consumers see one suppressible warning
// rather than a surprise at the type-resolution boundary.
[assembly: System.CLSCompliant(false)]
