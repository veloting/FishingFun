using System.Drawing;

namespace FishingFun
{
    public interface ICastAwareBobberFinder
    {
        void PrepareForCast();
    }

    public interface IBobberFinder
    {
        Point Find();

        void Reset();
    }
}
