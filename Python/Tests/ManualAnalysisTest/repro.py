import abc

class A:
    @abc.abstractmethod
    def virt():
        pass

class B(A):
    def virt():
        return 42

a = A()
b = a.virt()
