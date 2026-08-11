using Amazon.Lambda.Core;
using ScanPay.Utility.Model;
using System.Collections.Generic;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public static class SocialPostOperation
    {
        public static async Task<SocialConnectionsResponse> GetConnectionsAsync(
            string organizationID,
            ILambdaContext context)
        {
            var service =
                new SocialConnectionService();

            List<SocialConnectionDb> connections =
                await service.GetByOrganizationAsync(
                    organizationID,
                    context);

            return new SocialConnectionsResponse
            {
                SocialConnections =
                    connections.ConvertAll(
                        SocialConnectionFrontend.From),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialConnectionResponse> GetConnectionAsync(
            string organizationID,
            string connectionID,
            ILambdaContext context)
        {
            var service =
                new SocialConnectionService();

            SocialConnectionDb connection =
                await service.ReadAsync(
                    organizationID,
                    connectionID,
                    context);

            return new SocialConnectionResponse
            {
                SocialConnection =
                    SocialConnectionFrontend.From(
                        connection),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<LambdaResponse> DeleteConnectionAsync(
            string organizationID,
            string connectionID,
            ILambdaContext context)
        {
            var service =
                new SocialConnectionService();

            await service.DeleteAsync(
                organizationID,
                connectionID,
                context);

            return SuccessResponse();
        }

        public static Task<SocialConnectionAuthorizeResponse> AuthorizeConnectionAsync(
            SocialConnectionAuthorizeRequest request,
            ILambdaContext context)
        {
            var service =
                new SocialOAuthService();

            return service.AuthorizeAsync(
                request,
                context);
        }

        public static async Task<SocialConnectionResponse> CompleteConnectionCallbackAsync(
            SocialConnectionCallbackRequest request,
            ILambdaContext context)
        {
            var service =
                new SocialOAuthService();

            SocialConnectionDb connection =
                await service.CompleteCallbackAsync(
                    request,
                    context);

            return new SocialConnectionResponse
            {
                SocialConnection =
                    SocialConnectionFrontend.From(
                        connection),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostResponse> CreatePostAsync(
            CreateSocialPostRequest request,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostResponse
            {
                SocialPost =
                    await service.CreateAsync(
                        request,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostsResponse> GetPostsAsync(
            string organizationID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostsResponse
            {
                SocialPosts =
                    await service.GetByOrganizationAsync(
                        organizationID,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostResponse> GetPostAsync(
            string organizationID,
            string postID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostResponse
            {
                SocialPost =
                    await service.ReadAsync(
                        organizationID,
                        postID,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostResponse> UpdatePostAsync(
            UpdateSocialPostRequest request,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostResponse
            {
                SocialPost =
                    await service.UpdateAsync(
                        request,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<LambdaResponse> DeletePostAsync(
            string organizationID,
            string postID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            await service.DeleteAsync(
                organizationID,
                postID,
                context);

            return SuccessResponse();
        }

        public static async Task<SocialPostResponse> PublishPostAsync(
            string organizationID,
            string postID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostResponse
            {
                SocialPost =
                    await service.QueueForPublishAsync(
                        organizationID,
                        postID,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostResponse> SchedulePostAsync(
            ScheduleSocialPostRequest request,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostResponse
            {
                SocialPost =
                    await service.ScheduleAsync(
                        request,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostResponse> CancelPostAsync(
            string organizationID,
            string postID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostResponse
            {
                SocialPost =
                    await service.CancelAsync(
                        organizationID,
                        postID,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostStatusResponse> GetPostStatusAsync(
            string organizationID,
            string postID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostStatusResponse
            {
                SocialPost =
                    await service.ReadAsync(
                        organizationID,
                        postID,
                        context),

                Deliveries =
                    await service.GetDeliveriesAsync(
                        organizationID,
                        postID,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        public static async Task<SocialPostAnalyticsResponse> GetPostAnalyticsAsync(
            string organizationID,
            string postID,
            ILambdaContext context)
        {
            var service =
                new SocialPostService();

            return new SocialPostAnalyticsResponse
            {
                SocialPost =
                    await service.ReadAsync(
                        organizationID,
                        postID,
                        context),

                Summary =
                    await service.GetAnalyticsSummaryAsync(
                        organizationID,
                        postID,
                        context),

                Status =
                    "success",

                Code =
                    200
            };
        }

        private static LambdaResponse SuccessResponse()
        {
            return new LambdaResponse
            {
                Status =
                    "success",

                Code =
                    200
            };
        }
    }
}
