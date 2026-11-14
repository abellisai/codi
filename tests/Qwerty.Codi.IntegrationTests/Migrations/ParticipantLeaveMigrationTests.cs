using Microsoft.Data.Sqlite;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

public sealed class ParticipantLeaveMigrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_DaSchemaTredici_PreservaPartecipantiEValidaUscita()
    {
        using var database = TempSqliteDatabase.Create();
        var catalog = MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly);
        var runner = new MigrationRunner(database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(new MigrationCatalog(catalog.Scripts.Where(script => script.Version <= 13)), CancellationToken.None);
        await using var connection = await database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school (id,name,normalized_name,created_at_utc_ms,updated_at_utc_ms) VALUES ('s','Scuola','scuola',100,100);
            INSERT INTO classroom (id,school_id,name,normalized_name,school_year,created_at_utc_ms,updated_at_utc_ms)
                VALUES ('c','s','Terza','terza','2026/2027',100,100);
            INSERT INTO course (id,title,created_at_utc_ms,updated_at_utc_ms) VALUES ('co','Corso',100,100);
            INSERT INTO lesson (id,course_id,title,created_at_utc_ms,updated_at_utc_ms) VALUES ('l','co','Lezione',100,100);
            INSERT INTO admin_user (id,username,normalized_username,password_hash,security_stamp,is_active,failed_login_count,created_at_utc_ms,updated_at_utc_ms)
                VALUES ('a','Insegnante','insegnante','hash','stamp',1,0,100,100);
            INSERT INTO lesson_session (id,lesson_id,classroom_id,controller_admin_id,status,admissions_open,session_revision,class_default_json,started_at_utc_ms,created_at_utc_ms,updated_at_utc_ms,row_version)
                VALUES ('session','l','c','a','Active',1,2,'{}',100,100,100,1);
            INSERT INTO session_participant (id,session_id,display_name,admission_status,connection_epoch,joined_at_utc_ms,created_at_utc_ms,updated_at_utc_ms,row_version)
                VALUES ('p','session','Luca','Admitted',0,100,100,100,1);
            """;
        await command.ExecuteNonQueryAsync();
        await runner.ApplyAsync(catalog, CancellationToken.None);
        await runner.ApplyAsync(catalog, CancellationToken.None);
        command.CommandText = "SELECT display_name FROM session_participant WHERE id='p' AND left_at_utc_ms IS NULL";
        Assert.Equal("Luca", await command.ExecuteScalarAsync());
        command.CommandText = "UPDATE session_participant SET left_at_utc_ms=99 WHERE id='p'";
        var failure = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(19, failure.SqliteErrorCode);
        command.CommandText = "UPDATE session_participant SET left_at_utc_ms=101 WHERE id='p'";
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }
}
