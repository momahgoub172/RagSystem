using RagSystem.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace RagSystem.Ingestion.Sql
{
    public interface ISchemaDescriptionProvider
    {
        List<TableSchema> Enrich(List<TableSchema> tables);

    }
}
