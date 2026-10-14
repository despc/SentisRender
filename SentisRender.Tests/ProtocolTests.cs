using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SentisRender.Net;
using VRageMath;
using Xunit;

namespace SentisRender.Tests
{
    public class ProtocolTests
    {
        static ProtocolTests()
        {
            // the game makes the default model at its start; there is no game here
            if (ProtoBuf.Meta.TypeModel.Default == null) ProtoBuf.Meta.RuntimeTypeModel.Create(true);
        }

        // the game's SerializeToBinary/SerializeFromBinary are these two calls (MyAPIUtilities), on both sides
        private static byte[] Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return stream.ToArray();
            }
        }

        private static T Read<T>(byte[] data)
        {
            using (var stream = new MemoryStream(data)) return ProtoBuf.Serializer.Deserialize<T>(stream);
        }

        private static ShapeData Full()
        {
            var shape = new ShapeData();
            var i = 1;
            foreach (var field in typeof(ShapeData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value;
                if (field.FieldType == typeof(string)) value = "s" + i;
                else if (field.FieldType == typeof(double)) value = i + 0.5;
                else if (field.FieldType == typeof(float)) value = i + 0.25f;
                else if (field.FieldType == typeof(int)) value = i;
                else if (field.FieldType == typeof(uint)) value = (uint)i;
                else if (field.FieldType == typeof(bool)) value = true;
                else if (field.FieldType == typeof(ShapeKind)) value = ShapeKind.Capsule;
                else if (field.FieldType == typeof(ShapeStyle)) value = ShapeStyle.SolidAndWire;
                else throw new InvalidOperationException("A field of a type the test does not fill: " + field.Name);
                field.SetValue(shape, value);
                i++;
            }
            return shape;
        }

        [Fact]
        public void A_batch_comes_back_as_it_was_sent()
        {
            var shape = Full();
            var batch = new ServerBatch
            {
                Reset = true, Version = RenderNet.Version,
                Upserts = new List<ShapeData> { shape },
                Removes = new List<string> { "a", "b" },
                ClearPrefixes = new List<string> { "zone:" },
                Notices = new List<NoticeData> { new NoticeData { Id = "n", Text = "Захват 45%", Ms = 1000, Font = "Green" } }
            };
            var back = Read<ServerBatch>(Write(batch));
            Assert.True(back.Reset);
            Assert.True(back.Upserts.Single().SameAs(shape));
            Assert.Equal(new[] { "a", "b" }, back.Removes);
            Assert.Equal("zone:", back.ClearPrefixes.Single());
            Assert.Equal("Захват 45%", back.Notices.Single().Text);
            Assert.Equal("Green", back.Notices.Single().Font);
        }

        [Fact]
        public void Every_field_of_a_shape_is_on_the_wire_and_copied_and_compared()
        {
            var shape = Full();
            var back = Read<ShapeData>(Write(shape));
            foreach (var field in typeof(ShapeData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.Equal(field.GetValue(shape), field.GetValue(back));
                Assert.Equal(field.GetValue(shape), field.GetValue(shape.Clone()));
                var changed = shape.Clone();
                var value = field.GetValue(changed);
                field.SetValue(changed,
                    value is bool b ? !b :
                    value is string s ? s + "x" :
                    field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, Convert.ToInt32(value) + 1) :
                    Convert.ChangeType(Convert.ToDouble(value) + 1, field.FieldType));
                Assert.False(changed.SameAs(shape), field.Name + " is not compared");
            }
        }

        [Fact]
        public void A_hello_comes_back()
        {
            Assert.Equal(RenderNet.Version, Read<ClientHello>(Write(new ClientHello { Version = RenderNet.Version })).Version);
        }

        [Fact]
        public void A_color_keeps_its_four_channels()
        {
            var color = new Color(10, 200, 30, 77);
            Assert.Equal(color, RenderNet.Unpack(RenderNet.Pack(color)));
        }

        [Fact]
        public void An_empty_batch_says_so()
        {
            Assert.True(new ServerBatch().IsEmpty);
            Assert.False(new ServerBatch { Removes = new List<string> { "x" } }.IsEmpty);
            Assert.False(new ServerBatch { Reset = true }.IsEmpty);
        }
    }
}
