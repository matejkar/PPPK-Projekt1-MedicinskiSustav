# Projekt 1 — Medicinski sustav (Ishodi 1, 4, 5)

Konzolna .NET 8 aplikacija + **vlastiti ORM (MiniOrm)** + **PostgreSQL u Dockeru**.

Projekt pokriva relacijsku bazu, složeni konceptualni model i ORM

## Pokretanje

```bash
cd Projekt1-MedicinskiSustav
docker compose up -d
dotnet run --project src/MedicalApp/MedicalApp.csproj
```

Postgres sluša na **localhost:5433** (da ne sudara s lokalnim Postgresom na 5432).

Pri **prvom pokretanju** aplikacija:

1. uspoređuje klase entiteta sa shemom i izvršava migracije (code-first)
2. traži unos liječnika (jedina prilika — kasnije CRUD nad liječnicima nije dostupan)

U izborniku: `9` učitava demo pacijenta za obranu.

## Model

| Entitet | Relacije | CRUD |
|---|---|---|
| `Doctor` | 1:N Examination | samo seed pri prvom startu |
| `Address` | N:1 s Patient (boravište i prebivalište) | da |
| `Patient` | N:1 Address (2x), 1:N Illness, 1:N Examination | da |
| `Illness` | N:1 Patient, 1:N Medication | da |
| `Medication` | N:1 Illness (doza, jedinica, učestalost) | da |
| `Examination` | N:1 Patient, N:1 Doctor, tip CT/MR/ULTRA/EKG/ECHO/OKO/DERM/DENTA/MAMMO/EEG | da |

OIB je `CHAR(11) UNIQUE`. Tipovi pokrivaju INT, DECIMAL, FLOAT, VARCHAR, CHAR, TEXT, TIMESTAMPTZ i TIMESTAMP.

## MiniOrm — što demonstrirati na obrani

### Mapiranje (refleksija)

Atributi `[Table]`, `[Column]`, `[Key]`, `[Identity]`, `[NotNull]`, `[Unique]`, `[SqlDefault]`, `[ForeignKey]`, `[HasMany]`, `[BelongsTo]` čitaju se refleksijom u `MetadataCache`.

### Filtriranje (expression trees)

`db.Patients.Where(p => p.LastName.Contains("ić")).OrderBy(p => p.FirstName)` prevodi se u `WHERE` / `ORDER BY` s parametriziranim SQL-om (`WhereTranslator`).

### Change tracking

`ChangeTracker` drži snapshot originalnih vrijednosti. `SaveChanges()` radi `DetectChanges()` i generira `UPDATE` **samo za izmijenjene stupce**. Izbornik `7` to pokazuje bez spremanja.

### Eager vs lazy

| | Eager (`Include`) | Lazy (`LazyLoadList`) |
|---|---|---|
| Kad se učitava | Odmah, uz roditelja | Pri prvom pristupu kolekciji |
| SQL | dodatni `IN (...)` upiti | 1 upit po kolekciji |
| Prednost | predvidiv broj round-tripova | ne učitava što ne treba |
| Nedostatak | veći payload, unaprijed treba znati grafa | **N+1** ako se radi u petlji |

### Migracije

`MigrationRunner` čita `information_schema`, uspoređuje s klasama i generira UP/DOWN SQL. Stanje je u `__miniorm_migrations`.

Migracija **ne može** (ili ne smije) proći kad:

- dodajete `NOT NULL` stupac bez DEFAULT, a tablica već ima retke
- pretvarate tip nespojivo (npr. TEXT → INTEGER s nebrojčanim vrijednostima)
- dodajete UNIQUE po stupcu koji već ima duplikate
- DROP stupca/tablice bi prekršio FK (osim uz CASCADE)
- rollback ne može vratiti obrisane podatke

Pretpostavke generatora (dopuštene zadatkom): rename stupca tretira se kao drop+add; ne pokušava se heuristika preimenovanja.

---

## Obrana — Postgres koncepti (Ishod 1, minimalni)

### Arhitektura: WAL, checkpointer, vacuum

- **WAL (Write-Ahead Log)** — prije nego se stranica zapíše na disk, promjena ide u WAL. Nakon pada Postgres reprodukcijom WAL-a vraća stanje. To je temelj izdržljivosti (Durability) i repliciranja.
- **Checkpointer** — povremeno zapisuje prljave buffere na heap/datoteke i označava WAL lokaciju do koje je heap usklađen, da se pri restartu ne mora replayati cijeli WAL.
- **Vacuum** — MVCC ostavlja mrtve verzije redaka. `VACUUM` ih čisti, ažurira visibility map, sprječava transaktion ID wraparound. `VACUUM FULL` pretpakira tablicu (ekskluzivni lock). Autovacuum to radi u pozadini.

### ACID u Postgresu

- **Atomicity** — transakcija (`BEGIN`…`COMMIT`/`ROLLBACK`). MiniOrm `SaveChanges` sve INSERT/UPDATE/DELETE radi u jednoj transakciji.
- **Consistency** — constraints (PK, UNIQUE, FK, NOT NULL) + trigersi. OIB unique je primjer.
- **Isolation** — default `READ COMMITTED`. MVCC: čitači ne blokiraju pisce. `REPEATABLE READ` / `SERIALIZABLE` za jače garantije.
- **Durability** — `COMMIT` čeka `fsync` WAL-a (ovisno o `synchronous_commit`).

### Indeksi

B-tree indeks ubrzava `=` / raspon / `ORDER BY` kad je predikat **sargable** (npr. `oib = $1`). Neće se koristiti kad:

- funkcija na stupcu (`LOWER(oib) = ...`) bez funkcionalnog indeksa
- `LIKE '%ić'` (vodeći wildcard)
- selektivnost je loša (optimizer odabere seq scan)
- tablica je dovoljno mala da seq scan bude jeftiniji

OIB ima UNIQUE constraint → Postgres automatski stvara unique indeks.

### Konekcije

Svaka klijentska veza = backend proces. Skupo je otvarati vezu po upitu. MiniOrm koristi **Npgsql pooling** (`NpgsqlDataSource`): kontekst drži jednu fizičku vezu dok živi, pool je dijeli među kontekstima. Treba `Dispose` da se veza vrati u pool. `MaxPoolSize` sprječava preopterećenje.

---

## Struktura

```
Projekt1-MedicinskiSustav/
  docker-compose.yml
  MedicinskiSustav.sln
  src/MiniOrm/          # biblioteka ORM-a
  src/MedicalApp/       # konzolni CRUD
```
