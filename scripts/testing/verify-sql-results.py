"""Rechazo una ejecución SQL vacía, fallida u omitida a partir de sus TRX actuales."""
import collections
import pathlib
import sys
import xml.etree.ElementTree as ET

folder = pathlib.Path(sys.argv[1])
files = list(folder.glob("*.trx"))
if not files:
    raise SystemExit("Falta el reporte TRX de esta ejecución SQL.")
counts = collections.Counter()
seen_classes = set()
ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
required = {"Wallet.Tests.SqlTransferTests", "Wallet.Tests.SqlTransferAtomicityTests", "Wallet.Tests.CoordinatedSqlTransferTests", "Wallet.Tests.SqlPaymentTests"}
for path in files:
    root = ET.parse(path).getroot()
    definitions = {test.get("id"): test.find(ns + "TestMethod").get("className", "")
                   for test in root.iter(ns + "UnitTest") if test.find(ns + "TestMethod") is not None}
    for result in root.iter(ns + "UnitTestResult"):
        class_name = definitions.get(result.get("testId"), "")
        if not class_name.startswith("Wallet.Tests.") or "Sql" not in class_name.rsplit(".", 1)[-1]:
            raise SystemExit("El reporte no corresponde exclusivamente a pruebas SQL del proyecto.")
        seen_classes.add(class_name)
        counts[result.get("outcome", "Unknown")] += 1
print("Resultados SQL:", dict(counts))
if counts["Passed"] == 0 or any(outcome != "Passed" and count for outcome, count in counts.items()):
    raise SystemExit("La validación SQL requiere pruebas ejecutadas, aprobadas y ninguna omitida.")

if not required.issubset(seen_classes):
    raise SystemExit("Faltan suites financieras obligatorias en el reporte SQL.")
