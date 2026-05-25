#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Linq;
using System.Data.SQLite;
using System.Threading;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public class TrigramStringMap<T> : IDisposable, IEnumerable<T> where T : class {
        private static int last_db_id = 0;
        private readonly int db_id;
        private bool disposed;
        private readonly SQLiteConnection connection;
        private readonly string tableName;
        private List<T> backend;

        public TrigramStringMap(string name) {
            backend = new List<T>();
            this.db_id = Interlocked.Increment(ref last_db_id);
            this.tableName = $"{name}{db_id}";
            var connectionStringBuilder = new SQLiteConnectionStringBuilder { DataSource = ":memory:" };
            connection = new SQLiteConnection(connectionStringBuilder.ToString());
            connection.Open();
            connection.EnableExtensions(true);
            connection.LoadExtension("SQLite.Interop.dll", "sqlite3_fts5_init");
            using (var command = new SQLiteCommand($@"CREATE VIRTUAL TABLE [{tableName}] USING FTS5(key, tokenize=""trigram"");", connection)) {
                command.ExecuteNonQuery();
            }
        }

        ~TrigramStringMap() {
            Dispose(false);
        }

        public void Add(string key, T value) {
            using (var insertCommand = new SQLiteCommand($@"INSERT INTO [{tableName}](rowid, key) VALUES (?, ?)", connection)) {
                insertCommand.Parameters.AddWithValue(null, backend.Count);
                insertCommand.Parameters.AddWithValue(null, key);
                insertCommand.ExecuteNonQuery();
            }
            backend.Add(value);
        }

        public void AddRange(Func<T, string> keyGetter, IEnumerable<T> values) {
            using (var transaction = connection.BeginTransaction())
            using (var insertCommand = new SQLiteCommand($@"INSERT INTO [{tableName}](rowid, key) VALUES (?, ?);", connection)) {
                var rowIdParameter = insertCommand.CreateParameter();
                var valueParameter = insertCommand.CreateParameter();
                insertCommand.Parameters.AddRange(new SQLiteParameter[] { rowIdParameter, valueParameter });

                foreach (var value in values) {
                    rowIdParameter.Value = backend.Count;
                    valueParameter.Value = keyGetter(value);
                    insertCommand.ExecuteNonQuery();
                    backend.Add(value);
                }

                transaction.Commit();
            }
        }

        public IList<T> Query(string key, int? limit) {
            using (var queryCommand = new SQLiteCommand($@"SELECT [rowid] FROM [{tableName}] WHERE [key] MATCH ? LIMIT ?", connection)) {
                queryCommand.Parameters.AddWithValue(null, $"\"{key}\"");
                queryCommand.Parameters.AddWithValue(null, limit ?? int.MaxValue);
                using (var resultReader = queryCommand.ExecuteReader()) {
                    if (!resultReader.HasRows) {
                        return new List<T>();
                    }
                    var result = new List<T>();
                    while (resultReader.Read()) {
                        var rowId = Convert.ToInt32(resultReader["rowid"]);
                        var value = backend[rowId];
                        result.Add(value);
                    }
                    return result;
                }
            }
        }

        public T Lookup(string key) {
            using (var queryCommand = new SQLiteCommand($@"SELECT [rowid] FROM [{tableName}] WHERE [key] MATCH ? LIMIT 2", connection)) {
                queryCommand.Parameters.AddWithValue(null, $"\"{key}\"");
                using (var resultReader = queryCommand.ExecuteReader()) {
                    if (!resultReader.HasRows) {
                        return null;
                    }
                    if (!resultReader.Read()) {
                        throw new Exception("Read returned false unexpectedly");
                    }

                    var rowId = Convert.ToInt32(resultReader["rowid"]);
                    var value = backend[rowId];
                    if (resultReader.Read()) {
                        throw new DuplicateKeyException(key, $"Multiple values found matching {key}");
                    }
                    return value;
                }
            }
        }

        public List<string> QueryMatchingKeys(string key, int? limit) {
            using (var queryCommand = new SQLiteCommand($@"SELECT [key] FROM [{tableName}](?) LIMIT ?", connection)) {
                queryCommand.Parameters.AddWithValue(null, $"\"{key}\"");
                queryCommand.Parameters.AddWithValue(null, limit ?? int.MaxValue);
                using (var resultReader = queryCommand.ExecuteReader()) {
                    if (!resultReader.HasRows) {
                        return new List<string>();
                    }
                    var result = new List<string>();
                    while (resultReader.Read()) {
                        var value = (string)resultReader["key"];
                        result.Add(value);
                    }
                    return result;
                }
            }
        }

        public int Count => backend.Count;

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    try {
                        using (var dropTableCommand = new SQLiteCommand($@"DROP TABLE IF EXISTS [{tableName}]", connection)) {
                            dropTableCommand.ExecuteNonQuery();
                        }
                    } catch (Exception e) {
                        Logger.Error($"Failed to drop {tableName}", e);
                    }

                    backend = null;
                    connection.Close();
                }
                disposed = true;
            }
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public IEnumerator<T> GetEnumerator() {
            return backend.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return backend.GetEnumerator();
        }
    }
}