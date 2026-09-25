-- Idempotent privilege layout. Runs at first init and after every restore (deploy/scripts/restore.sh).
DO $$
DECLARE s text;
BEGIN
  FOREACH s IN ARRAY ARRAY['finance', 'auth'] LOOP
    EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION finance_migrator', s);
    EXECUTE format('ALTER SCHEMA %I OWNER TO finance_migrator', s);
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO finance_app, finance_backup', s);
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO finance_app', s);
    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO finance_app', s);
    EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO finance_backup', s);
    EXECUTE format('GRANT SELECT ON ALL SEQUENCES IN SCHEMA %I TO finance_backup', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE finance_migrator IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO finance_app', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE finance_migrator IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO finance_app', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE finance_migrator IN SCHEMA %I GRANT SELECT ON TABLES TO finance_backup', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE finance_migrator IN SCHEMA %I GRANT SELECT ON SEQUENCES TO finance_backup', s);
  END LOOP;
END
$$;
