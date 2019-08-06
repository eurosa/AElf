using System;
using System.Threading;
using System.Threading.Tasks;
using AElf.Cryptography;
using AElf.Kernel;
using AElf.Kernel.Blockchain.Application;
using AElf.Modularity;
using AElf.OS.Network.Grpc;
using AElf.OS.Network.Infrastructure;
using AElf.Types;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NSubstitute;
using Volo.Abp.Modularity;

namespace AElf.OS.Network
{
    [DependsOn(typeof(OSCoreWithChainTestAElfModule), typeof(GrpcNetworkModule))]
    public class GrpcBasicNetworkTestModule : AElfModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var netTestHelper = new NetworkTestContextHelpers();
            context.Services.AddSingleton(netTestHelper);
                
            Configure<NetworkOptions>(o => {
                o.ListeningPort = 2001;
                o.MaxPeers = 2;
            });

            context.Services.AddTransient(sp =>
            {
                var mockDialer = new Mock<IPeerDialer>();
                
                mockDialer.Setup(d => d.DialPeerAsync(It.Is<string>(ip => ip == NetworkTestConstants.FakeIpEndpoint)))
                    .Returns<string>(s =>
                    {
                        var peer = GrpcTestPeerHelpers.CreateBasicPeer(NetworkTestConstants.FakeIpEndpoint, NetworkTestConstants.FakePubkey);
                        netTestHelper.AddDialedPeer(peer);
                        return Task.FromResult(peer);
                    });
                
                mockDialer.Setup(d => d.DialPeerAsync(It.Is<string>(ip => ip == NetworkTestConstants.FakeIpEndpoint2)))
                    .Returns<string>(s =>
                    {
                        var peer = GrpcTestPeerHelpers.CreateBasicPeer(NetworkTestConstants.FakeIpEndpoint2, NetworkTestConstants.FakePubkey);
                        netTestHelper.AddDialedPeer(peer);
                        return Task.FromResult(peer);
                    });
                
                mockDialer.Setup(d => d.DialPeerAsync(It.Is<string>(ip => ip == NetworkTestConstants.DialExceptionIpEndpoint)))
                    .Throws<PeerDialException>();

                mockDialer.Setup(d => d.DialPeerAsync(It.Is<string>(ip => ip == NetworkTestConstants.HandshakeWithNetExceptionIp)))
                    .Returns<string>(s =>
                    {
                        var mockClient = new Mock<PeerService.PeerServiceClient>();
                        mockClient.Setup(m => m.DoHandshakeAsync(It.IsAny<HandshakeRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), CancellationToken.None))
                            .Throws(new AggregateException());
                        
                        var peer = GrpcTestPeerHelpers.CreatePeerWithClient(NetworkTestConstants.FakeIpEndpoint2, NetworkTestConstants.FakePubkey, 
                            mockClient.Object);
                        
                        netTestHelper.AddDialedPeer(peer);
                        
                        return Task.FromResult(peer);
                    });
                
                // Incorrect handshake
                mockDialer.Setup(d => d.DialPeerAsync(It.Is<string>(ip => ip == NetworkTestConstants.BadHandshakeIp)))
                    .Returns<string>((s) =>
                    {
                        var handshakeReply = new HandshakeReply();
                        
                        var handshakeCall = TestCalls.AsyncUnaryCall(Task.FromResult(handshakeReply), 
                            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
                            
                        var mockClient = new Mock<PeerService.PeerServiceClient>();
                        mockClient.Setup(m => m.DoHandshakeAsync(It.IsAny<HandshakeRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), 
                            CancellationToken.None)).Returns(handshakeCall);
                        
                        var peer = GrpcTestPeerHelpers.CreatePeerWithClient(NetworkTestConstants.GoodPeerEndpoint,
                            NetworkTestConstants.FakePubkey, mockClient.Object);
                            
                        netTestHelper.AddDialedPeer(peer);
                            
                        return Task.FromResult(peer);
                    });
                    
                    // This peer will pass all checks with success.
                    mockDialer.Setup(d => d.DialPeerAsync(It.Is<string>(ip => ip == NetworkTestConstants.GoodPeerEndpoint)))
                        .Returns<string>(s =>
                        {
                            var keyPair = CryptoHelper.GenerateKeyPair();
                            var handshakeReply = new HandshakeReply {
                                Handshake = netTestHelper.CreateValidHandshake(keyPair, 10)
                            };
                            var handshakeCall = TestCalls.AsyncUnaryCall(Task.FromResult(handshakeReply), 
                                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
                            
                            var mockClient = new Mock<PeerService.PeerServiceClient>();
                            mockClient.Setup(m => m.DoHandshakeAsync(It.IsAny<HandshakeRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), 
                                    CancellationToken.None)).Returns(handshakeCall);
                        
                            var peer = GrpcTestPeerHelpers.CreatePeerWithClient(NetworkTestConstants.GoodPeerEndpoint,
                                keyPair.PublicKey.ToHex(), mockClient.Object);
                            
                            netTestHelper.AddDialedPeer(peer);
                            
                            return Task.FromResult(peer);
                    });

                return mockDialer.Object;
            });
        }
    }
}