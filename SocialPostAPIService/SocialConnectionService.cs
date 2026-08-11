using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class SocialConnectionService
    {
        public async Task<SocialConnectionDb> CreateAsync(
            SocialConnectionDb connection,
            ILambdaContext context)
        {
            ValidateConnection(
                connection);

            connection.SocialConnectionID =
                FormatValue.NotEmptyValue(connection.SocialConnectionID)
                    ? connection.SocialConnectionID
                    : FormatValue.NewID();

            connection.Status =
                FormatValue.NotEmptyValue(connection.Status)
                    ? connection.Status
                    : SocialConnectionStatus.Connected;

            connection.CreateDate =
                DefaultValue.UtcNow();

            connection.LastUpdate =
                DefaultValue.UtcNow();

            await DbCRUD<SocialConnectionDb>.CreateAsync(
                connection,
                context);

            return connection;
        }

        public async Task<SocialConnectionDb> ReadAsync(
            string organizationID,
            string socialConnectionID,
            ILambdaContext context)
        {
            if (FormatValue.EmptyValue(socialConnectionID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "connection_id is required.");
            }

            SocialConnectionDb connection =
                await DbCRUD<SocialConnectionDb>.ReadAsync(
                    socialConnectionID,
                    context);

            if (connection == null)
            {
                throw ResponseStatusFactory.NotFound(
                    $"Social connection({socialConnectionID}) was not found.");
            }

            EnsureOrganizationOwnsConnection(
                organizationID,
                connection);

            return connection;
        }

        public async Task<List<SocialConnectionDb>> GetByOrganizationAsync(
            string organizationID,
            ILambdaContext context)
        {
            ValidateOrganizationID(
                organizationID);

            var attrValues =
                new Dictionary<string, AttributeValue>
                {
                    [":v_OrganizationID"] =
                        new(organizationID)
                };

            QueryRequest request =
                new()
                {
                    TableName =
                        "SocialConnection",

                    IndexName =
                        "OrganizationID-index",

                    KeyConditionExpression =
                        "OrganizationID = :v_OrganizationID",

                    ExpressionAttributeValues =
                        attrValues
                };

            var connections =
                new List<SocialConnectionDb>();

            await foreach (SocialConnectionDb connection in
                           DbCRUD<SocialConnectionDb>.QueryAsync(
                               request,
                               context))
            {
                connections.Add(
                    connection);
            }

            return connections
                .OrderByDescending(connection =>
                    connection.CreateDate)
                .ToList();
        }

        public async Task DeleteAsync(
            string organizationID,
            string socialConnectionID,
            ILambdaContext context)
        {
            SocialConnectionDb connection =
                await ReadAsync(
                    organizationID,
                    socialConnectionID,
                    context);

            connection.Status =
                SocialConnectionStatus.Disconnected;

            connection.LastUpdate =
                DefaultValue.UtcNow();

            await DbCRUD<SocialConnectionDb>.SaveAsync(
                connection,
                context);
        }

        private static void ValidateConnection(
            SocialConnectionDb connection)
        {
            if (connection == null)
            {
                throw ResponseStatusFactory.BadRequest(
                    "Request is required.");
            }

            ValidateOrganizationID(
                connection.OrganizationID);

            if (!SocialPlatform.IsValid(connection.Platform))
            {
                throw ResponseStatusFactory.BadRequest(
                    "platform is invalid.");
            }

            if (FormatValue.EmptyValue(connection.TokenSecretID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "token_secret_id is required.");
            }
        }

        private static void ValidateOrganizationID(
            string organizationID)
        {
            if (FormatValue.EmptyValue(organizationID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "organization_id is required.");
            }
        }

        private static void EnsureOrganizationOwnsConnection(
            string organizationID,
            SocialConnectionDb connection)
        {
            ValidateOrganizationID(
                organizationID);

            if (!string.Equals(
                    connection.OrganizationID,
                    organizationID,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw ResponseStatusFactory.Forbidden(
                    "Social connection does not belong to this organization.");
            }
        }
    }
}
