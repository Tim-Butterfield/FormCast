// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.IO;

using FormCast.Ipc;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the shared FormCast.Ipc.PipeProtocol framing
 /// and message builders. Pure logic, no I/O beyond MemoryStream.
 /// </summary>
 public class PipeProtocolTests
 {
 // -----------------------------------------------------------------
 // Length-prefixed framing
 // -----------------------------------------------------------------

 [Fact]
 public void WriteFrame_then_ReadFrame_round_trips_payload_bytes()
 {
 byte[] payload = new byte[] { 0x42, 0x01, 0x02, 0x03 };
 using var ms = new MemoryStream();
 PipeProtocol.WriteFrame(ms, payload);
 ms.Position = 0;
 byte[]? back = PipeProtocol.ReadFrame(ms);
 Assert.NotNull(back);
 Assert.Equal(payload, back);
 }

 [Fact]
 public void ReadFrame_returns_null_on_clean_eof()
 {
 using var ms = new MemoryStream();
 // Empty stream: no length header, no payload.
 byte[]? back = PipeProtocol.ReadFrame(ms);
 Assert.Null(back);
 }

 [Fact]
 public void ReadFrame_throws_on_zero_length_header()
 {
 using var ms = new MemoryStream(new byte[] { 0, 0, 0, 0 });
 Assert.Throws<InvalidDataException>(() => PipeProtocol.ReadFrame(ms));
 }

 [Fact]
 public void ReadFrame_throws_on_oversized_header()
 {
 // Header claims MaxPayloadBytes + 1.
 int over = PipeProtocol.MaxPayloadBytes + 1;
 byte[] header = new byte[]
 {
 (byte)(over & 0xFF),
 (byte)((over >> 8) & 0xFF),
 (byte)((over >> 16) & 0xFF),
 (byte)((over >> 24) & 0xFF),
 };
 using var ms = new MemoryStream(header);
 Assert.Throws<InvalidDataException>(() => PipeProtocol.ReadFrame(ms));
 }

 [Fact]
 public void WriteFrame_rejects_empty_payload()
 {
 using var ms = new MemoryStream();
 Assert.Throws<ArgumentException>(() =>
 PipeProtocol.WriteFrame(ms, Array.Empty<byte>()));
 }

 [Fact]
 public void WriteFrame_rejects_oversized_payload()
 {
 using var ms = new MemoryStream();
 byte[] huge = new byte[PipeProtocol.MaxPayloadBytes + 1];
 huge[0] = 0x01;
 Assert.Throws<ArgumentException>(() => PipeProtocol.WriteFrame(ms, huge));
 }

 // -----------------------------------------------------------------
 // BuildPipeName
 // -----------------------------------------------------------------

 [Fact]
 public void BuildPipeName_concatenates_prefix_and_session_id()
 {
 Assert.Equal("FormCast.Host.42", PipeProtocol.BuildPipeName("42"));
 }

 // -----------------------------------------------------------------
 // Hello round trips
 // -----------------------------------------------------------------

 [Fact]
 public void HelloRequest_round_trip_preserves_version_and_name()
 {
 byte[] req = PipeProtocol.BuildHelloRequest("FormCast.Plugin");
 (uint v, string name) = PipeProtocol.ParseHelloRequest(req);
 Assert.Equal(PipeProtocol.ProtocolVersion, v);
 Assert.Equal("FormCast.Plugin", name);
 }

 [Fact]
 public void HelloResponse_round_trip_preserves_status_version_and_name()
 {
 byte[] resp = PipeProtocol.BuildHelloResponse(
 PipeProtocol.HelloOk, "FormCast.Host");
 (uint v, byte status, string name) = PipeProtocol.ParseHelloResponse(resp);
 Assert.Equal(PipeProtocol.ProtocolVersion, v);
 Assert.Equal(PipeProtocol.HelloOk, status);
 Assert.Equal("FormCast.Host", name);
 }

 [Fact]
 public void HelloResponse_with_mismatch_status_round_trips_status_byte()
 {
 byte[] resp = PipeProtocol.BuildHelloResponse(
 PipeProtocol.HelloVersionMismatch, "FormCast.Host");
 (_, byte status, _) = PipeProtocol.ParseHelloResponse(resp);
 Assert.Equal(PipeProtocol.HelloVersionMismatch, status);
 }

 [Fact]
 public void HelloRequest_rejects_oversized_name()
 {
 string longName = new string('x', 256);
 Assert.Throws<ArgumentException>(() =>
 PipeProtocol.BuildHelloRequest(longName));
 }

 [Fact]
 public void ParseHelloRequest_throws_on_truncated_payload()
 {
 byte[] truncated = new byte[] { PipeProtocol.MsgHelloRequest, 0x01, 0x00, 0x00 };
 Assert.Throws<InvalidDataException>(() =>
 PipeProtocol.ParseHelloRequest(truncated));
 }

 // -----------------------------------------------------------------
 // Ping / Pong
 // -----------------------------------------------------------------

 [Fact]
 public void Ping_round_trip_preserves_nonce()
 {
 byte[] ping = PipeProtocol.BuildPing(0xDEADBEEF);
 uint n = PipeProtocol.ParsePing(ping);
 Assert.Equal(0xDEADBEEFu, n);
 }

 [Fact]
 public void Pong_round_trip_preserves_nonce()
 {
 byte[] pong = PipeProtocol.BuildPong(0x12345678);
 uint n = PipeProtocol.ParsePong(pong);
 Assert.Equal(0x12345678u, n);
 }

 [Fact]
 public void ParsePing_rejects_pong_payload()
 {
 byte[] pong = PipeProtocol.BuildPong(1);
 Assert.Throws<InvalidDataException>(() => PipeProtocol.ParsePing(pong));
 }

 // -----------------------------------------------------------------
 // Goodbye
 // -----------------------------------------------------------------

 [Fact]
 public void BuildGoodbye_is_a_single_type_byte()
 {
 byte[] bye = PipeProtocol.BuildGoodbye();
 Assert.Single(bye);
 Assert.Equal(PipeProtocol.MsgGoodbye, bye[0]);
 }
 }
}
